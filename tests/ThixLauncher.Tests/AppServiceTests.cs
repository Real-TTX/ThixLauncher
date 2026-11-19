using System.Net;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tests;

/// <summary>
/// The business layer of the apps against GitHub and a computer in memory: the feeds, installing, updating, removing and
/// starting, and what it refuses (policy, state, a package without or with a wrong checksum).
/// </summary>
public sealed class AppServiceTests
{
    [Fact]
    public async Task The_feeds_name_the_newest_build_of_each_app_with_its_checksum()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();

        var clip = AppService.Get("thixclip");
        Assert.Equal("0.1.12-20261115", clip.Latest!.Version.Text);
        Assert.Equal(12, clip.Latest.Version.BuildNumber);
        Assert.Equal(64, clip.Latest.Sha256!.Length);
        Assert.Contains("/releases/download/release-channel/", clip.Latest.DownloadUrl);

        var note = AppService.Get("thixnote");
        Assert.Equal("1.1.1-20261116", note.Latest!.Version.Text);
        Assert.Matches(@"^ThixNote(Setup\.exe|-macOS-(arm64|x64)\.zip)$", note.Latest.PackageFile);
        Assert.Contains("/releases/download/release-channel/", note.Latest.DownloadUrl);
        Assert.Equal(64, note.Latest.Sha256!.Length);
        Assert.Equal(AppState.NotInstalled, note.State);
        Assert.NotNull(AppService.LastRefresh);
    }

    [Fact]
    public async Task The_nightly_channel_offers_the_nightly_builds()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { AppChannel = AppChannel.Nightly });
        await AppService.RefreshAsync();

        Assert.Equal("nightly-14-20261116", AppService.Get("thixclip").Latest!.Version.Text);
        Assert.Equal("nightly-5-20261116", AppService.Get("thixnote").Latest!.Version.Text);
        Assert.Contains(data.GitHub.Requests, url => url.Contains("/nightly-channel/thixclip-update.json"));
    }

    [Fact]
    public async Task After_a_change_of_the_channel_the_old_builds_no_longer_count()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        await AppService.RefreshAsync();
        Assert.True(AppService.Get("thixclip").HasUpdate);

        SettingsService.Update(settings => settings with { AppChannel = AppChannel.Nightly });
        Assert.Null(AppService.Get("thixclip").Latest);
        Assert.Equal(AppState.Installed, AppService.Get("thixclip").State);
    }

    [Fact]
    public async Task A_feed_that_fails_keeps_its_last_answer_and_names_the_problem()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        data.GitHub.FailWith = HttpStatusCode.Forbidden;
        await AppService.RefreshAsync();

        var note = AppService.Get("thixnote");
        Assert.Equal("1.1.1-20261116", note.Latest!.Version.Text);
        Assert.Contains("Limit", note.FeedError);
    }

    [Fact]
    public async Task A_channel_without_builds_offers_nothing_to_install()
    {
        using var data = new TestData();
        data.GitHub.Versions.Remove(("thixnote", AppVersion.Release));
        await AppService.RefreshAsync();

        var note = AppService.Get("thixnote");
        Assert.Null(note.Latest);
        Assert.Null(note.FeedError);
        var refused = await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixnote"));
        Assert.Contains("noch keinen Build", refused.Message);
    }

    [Fact]
    public async Task Installing_downloads_checks_and_installs_the_newest_build()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        var states = new List<AppState>();
        AppService.Changed += Record;
        try
        {
            await AppService.InstallAsync("thixclip");
        }
        finally
        {
            AppService.Changed -= Record;
        }

        var clip = AppService.Get("thixclip");
        Assert.Equal(AppState.Installed, clip.State);
        Assert.Equal("0.1.12-20261115", clip.Installed!.VersionText);
        Assert.Contains(AppState.Installing, states);
        Assert.Contains(data.GitHub.Requests, url => url.EndsWith("/release-channel/" + clip.Latest!.PackageFile, StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(AppPaths.DownloadDirectory, "thixclip")), "the package is deleted after the installation");

        void Record(object? sender, EventArgs e) => states.Add(AppService.Get("thixclip").State);
    }

    [Fact]
    public async Task An_installed_app_with_a_newer_build_is_updated()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixnote")!, "1.0.1-20261115");
        await AppService.RefreshAsync();
        Assert.Equal(AppState.UpdateAvailable, AppService.Get("thixnote").State);
        Assert.Equal(1, AppService.Count(AppFilter.Updates));

        await AppService.InstallAsync("thixnote");
        Assert.Equal("1.1.1-20261116", AppService.Get("thixnote").Installed!.VersionText);
        Assert.Equal(0, AppService.Count(AppFilter.Updates));

        var refused = await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixnote"));
        Assert.Contains("schon aktuell", refused.Message);
    }

    [Fact]
    public async Task A_damaged_download_is_not_installed()
    {
        using var data = new TestData();
        data.GitHub.BreakChecksums = true;
        await AppService.RefreshAsync();

        var refused = await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixclip"));
        Assert.Contains("Prüfsumme", refused.Message);
        Assert.False(AppService.Get("thixclip").IsInstalled);
        Assert.False(AppService.Get("thixclip").IsBusy);
    }

    [Fact]
    public async Task A_package_without_a_checksum_is_never_installed()
    {
        using var data = new TestData();
        data.GitHub.OmitDigests = true;
        await AppService.RefreshAsync();

        var refused = await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixnote"));
        Assert.Contains("keine Prüfsumme", refused.Message);
        Assert.DoesNotContain(data.GitHub.Requests, url => !url.EndsWith(".json", StringComparison.Ordinal) && url.Contains("/releases/download/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_action_per_app_at_a_time()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        data.System.Hold = new TaskCompletionSource();
        var first = AppService.InstallAsync("thixclip");
        await WaitUntil(() => AppService.Get("thixclip").State == AppState.Installing && data.GitHub.Requests.Count >= 3);

        Assert.True(AppService.Get("thixclip").IsBusy);
        Assert.False(AppService.Get("thixclip").CanLaunch);
        var second = await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixclip"));
        Assert.Contains("gerade geändert", second.Message);

        data.System.Hold.SetResult();
        await first;
        Assert.Equal(AppState.Installed, AppService.Get("thixclip").State);
    }

    [Fact]
    public async Task Removing_takes_the_app_away_and_starting_needs_it_installed()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.12-20261115");
        await AppService.RefreshAsync();

        AppService.Launch("thixclip");
        Assert.Equal(["thixclip"], data.System.Launched);

        await AppService.UninstallAsync("thixclip");
        Assert.False(AppService.Get("thixclip").IsInstalled);
        Assert.Contains("nicht installiert", Assert.Throws<BusinessException>(() => AppService.Launch("thixclip")).Message);
        Assert.Contains("nicht installiert", (await Assert.ThrowsAsync<BusinessException>(() => AppService.UninstallAsync("thixclip"))).Message);
    }

    [Fact]
    public async Task The_policy_DisableAppChanges_allows_starting_but_no_changes()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        data.Policies.Add("DisableAppChanges");
        await AppService.RefreshAsync();

        Assert.Contains("DisableAppChanges", (await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixnote"))).Message);
        Assert.Contains("DisableAppChanges", (await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixclip"))).Message);
        Assert.Contains("DisableAppChanges", (await Assert.ThrowsAsync<BusinessException>(() => AppService.UninstallAsync("thixclip"))).Message);
        Assert.Contains("DisableAppChanges", (await Assert.ThrowsAsync<BusinessException>(() => AppService.UpdateAllAsync(quietOnly: true))).Message);
        AppService.Launch("thixclip");
        Assert.Equal(["thixclip"], data.System.Launched);
        Assert.Equal("0.1.9-20261110", AppService.Get("thixclip").Installed!.VersionText);
    }

    [Fact]
    public async Task Updating_all_leaves_out_the_setups_that_ask_when_nobody_is_there()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        data.System.Add(AppCatalog.Find("thixnote")!, "1.0.1-20261115");
        await AppService.RefreshAsync();

        var report = await AppService.UpdateAllAsync(quietOnly: true);
        Assert.Contains("ThixClip", report.Updated);
        Assert.Equal(AppCatalog.Find("thixnote")!.InstallsQuietly, report.Updated.Contains("ThixNote"));
        Assert.Empty(report.Failed);
        Assert.Equal("0.1.12-20261115", AppService.Get("thixclip").Installed!.VersionText);
    }

    [Fact]
    public async Task The_list_searches_filters_and_sorts()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixnote")!, "1.0.1-20261115");
        await AppService.RefreshAsync();

        Assert.Equal(["ThixCalc", "ThixCalendar", "ThixClip", "ThixFred", "ThixNote", "ThixShow", "ThixTunnel", "ThixWrite"], Names(new AppQuery()));
        Assert.Equal(["ThixNote", "ThixCalc", "ThixCalendar", "ThixClip", "ThixFred", "ThixShow", "ThixTunnel", "ThixWrite"], Names(new AppQuery(Sort: AppSort.State)));
        Assert.Equal(["ThixNote"], Names(new AppQuery(Filter: AppFilter.Installed)));
        Assert.Equal(["ThixNote"], Names(new AppQuery(Filter: AppFilter.Updates)));
        Assert.Equal(["ThixCalc", "ThixCalendar", "ThixClip", "ThixFred", "ThixShow", "ThixTunnel", "ThixWrite"], Names(new AppQuery(Filter: AppFilter.NotInstalled)));
        Assert.Equal(["ThixFred"], Names(new AppQuery("terminal")));
        Assert.Equal(["ThixTunnel"], Names(new AppQuery("ssh")));
        Assert.Equal(["ThixClip"], Names(new AppQuery("zwischenablage")));
        Assert.Equal(["ThixNote"], Names(new AppQuery("markdown")));
        Assert.Equal(["ThixCalendar"], Names(new AppQuery("kalender")));
        Assert.Equal(["ThixWrite"], Names(new AppQuery("dokument")));
        Assert.Equal(["ThixCalc"], Names(new AppQuery("tabellen")));
        Assert.Equal(["ThixShow"], Names(new AppQuery("präsentation")));
        Assert.Equal(["ThixShow"], Names(new AppQuery("powerpoint")));
        Assert.Equal(["ThixShow"], Names(new AppQuery("folien")));
        Assert.Equal(["ThixShow"], Names(new AppQuery("pptx")));

        static List<string> Names(AppQuery query) => AppService.Query(query).Select(status => status.App.Name).ToList();
    }

    [Fact]
    public async Task Two_apps_of_one_repository_each_get_their_own_manifest_and_package()
    {
        using var data = new TestData();
        var write = OfficeApp("thixwrite", "ThixWrite");
        var calc = OfficeApp("thixcalc", "ThixCalc");
        var github = new SampleGitHub([write, calc]);
        github.Versions[("thixwrite", AppVersion.Release)] = "0.1.4-20261119";
        github.Versions[("thixcalc", AppVersion.Release)] = "0.2.7-20261118";
        github.Versions[("thixcalc", AppVersion.Nightly)] = "nightly-9-20261119";
        ReleaseFeed.HandlerForTests = github;

        var writeRelease = (await ReleaseFeed.LatestAsync(write, AppVersion.Release, "win-x64", CancellationToken.None))!;
        var calcRelease = (await ReleaseFeed.LatestAsync(calc, AppVersion.Release, "osx-arm64", CancellationToken.None))!;
        Assert.Equal("0.1.4-20261119", writeRelease.Version.Text);
        Assert.Equal("ThixWriteSetup.exe", writeRelease.PackageFile);
        Assert.Equal("https://github.com/Real-TTX/ThixOffice/releases/download/release-channel/ThixWriteSetup.exe", writeRelease.DownloadUrl);
        Assert.Equal("0.2.7-20261118", calcRelease.Version.Text);
        Assert.Equal("ThixCalc-macOS-arm64.zip", calcRelease.PackageFile);
        Assert.Contains(github.Requests, url => url.EndsWith("/release-channel/thixwrite-update.json", StringComparison.Ordinal));
        Assert.Contains(github.Requests, url => url.EndsWith("/release-channel/thixcalc-update.json", StringComparison.Ordinal));

        // the nightly of one app is not the nightly of the other
        Assert.Null(await ReleaseFeed.LatestAsync(write, AppVersion.Nightly, "win-x64", CancellationToken.None));
        Assert.Equal("nightly-9-20261119", (await ReleaseFeed.LatestAsync(calc, AppVersion.Nightly, "win-x64", CancellationToken.None))!.Version.Text);

        // each package is the one of its app, and its checksum fits
        foreach (var (app, release) in new[] { (write, writeRelease), (calc, calcRelease) })
        {
            var file = Path.Combine(data.Folder, release.PackageFile);
            await ReleaseFeed.DownloadAsync(release.DownloadUrl, file, release.Sha256!, null, CancellationToken.None);
            Assert.Equal((app.Id, release.Version.Text), SampleGitHub.ReadPackage(file));
        }
    }

    [Fact]
    public async Task An_app_with_single_releases_is_read_from_the_release_list_of_its_repository()
    {
        using var data = new TestData();
        var app = OfficeApp("thixwrite", "ThixWrite") with
        {
            Repository = "Real-TTX/ThixWrite",
            Feed = FeedKind.GitHubReleases,
            PackagePatterns = new Dictionary<string, string>
            {
                ["win-x64"] = @"^ThixWrite-Setup-.+-win-x64\.exe$",
                ["osx-arm64"] = @"^ThixWrite-.+-osx-arm64-portable\.zip$",
                ["osx-x64"] = @"^ThixWrite-.+-osx-x64-portable\.zip$",
            },
        };
        var github = new SampleGitHub([app]);
        github.Versions[("thixwrite", AppVersion.Release)] = "0.1.4-20261119";
        ReleaseFeed.HandlerForTests = github;

        var release = (await ReleaseFeed.LatestAsync(app, AppVersion.Release, "win-x64", CancellationToken.None))!;
        Assert.Equal("ThixWrite-Setup-0.1.4-20261119-win-x64.exe", release.PackageFile);
        Assert.Equal("https://github.com/Real-TTX/ThixWrite/releases/tag/v0.1.4-20261119", release.ReleasePage);

        var file = Path.Combine(data.Folder, release.PackageFile);
        await ReleaseFeed.DownloadAsync(release.DownloadUrl, file, release.Sha256!, null, CancellationToken.None);
        Assert.Equal(("thixwrite", "0.1.4-20261119"), SampleGitHub.ReadPackage(file));
    }

    [Fact]
    public void An_unknown_app_is_refused()
    {
        using var data = new TestData();
        Assert.Throws<BusinessException>(() => AppService.Get("thixunknown"));
    }

    /// <summary>An app of a suite that publishes several apps from one repository (ThixOffice), not in the catalog.</summary>
    private static ThixApp OfficeApp(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Tagline = name,
        Description = name,
        Icon = "file-text",
        Repository = "Real-TTX/ThixOffice",
        Feed = FeedKind.ChannelManifest,
        WindowsExecutable = name + ".exe",
        WindowsUninstallKey = name,
        WindowsQuietInstallArguments = "--update",
        MacBundleName = name + ".app",
    };

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var round = 0; round < 200 && !condition(); round++)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "the condition was not met in time");
    }
}
