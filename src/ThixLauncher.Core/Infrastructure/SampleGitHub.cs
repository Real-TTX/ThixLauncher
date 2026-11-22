using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Infrastructure;

/// <summary>
/// GitHub in memory, for the tests, the self test and the snapshots (<see cref="ReleaseFeed.HandlerForTests"/>): it answers
/// the manifests of the rolling channels (every app of the catalog and ThixLauncher), the release list of the GitHub API (an app
/// with <see cref="FeedKind.GitHubReleases"/>), the downloads, the pictures of the store (raw.githubusercontent.com, with ETag) and
/// the commit feeds (github.com/…/commits/&lt;branch&gt;.atom), with the versions set in <see cref="Versions"/>. A package is a small text whose SHA-256 the feeds declare,
/// so the whole way – feed, download, checksum, install – runs as it does with the real GitHub. Never used by the real app.
/// Several apps may publish from one repository (ThixOffice): the manifest's file name and the package's file name say which app is meant.
/// </summary>
internal sealed class SampleGitHub(IReadOnlyList<ThixApp>? apps = null) : HttpMessageHandler
{
    /// <summary>ThixLauncher's own updates, answered like an app (<see cref="UpdateService"/>).</summary>
    private static readonly ThixApp Launcher = new()
    {
        Id = "thixlauncher",
        Name = "ThixLauncher",
        Tagline = "",
        Description = "",
        Icon = "rocket",
        Repository = AppInfo.Repository,
        Feed = FeedKind.ChannelManifest,
        ManifestFileName = UpdateService.ManifestFileName,
        WindowsExecutable = "ThixLauncher.exe",
        WindowsUninstallKey = "ThixLauncher",
        MacBundleName = "ThixLauncher.app",
    };

    /// <summary>The apps it publishes: the catalog, or the apps a test gives (e.g. two apps of one repository).</summary>
    public IReadOnlyList<ThixApp> Apps { get; } = apps ?? AppCatalog.Apps;

    /// <summary>The newest version per app id and channel ("thixclip", "release" → "0.1.12-20261115"); missing = the channel has none.</summary>
    public Dictionary<(string AppId, string Channel), string> Versions { get; } = new();

    /// <summary>The time every release was published.</summary>
    public DateTimeOffset PublishedAt { get; set; } = new(2026, 11, 15, 21, 51, 0, TimeSpan.Zero);

    /// <summary>Answer every request with this status instead (GitHub down, the rate limit).</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Declare a wrong checksum for every package (a damaged download).</summary>
    public bool BreakChecksums { get; set; }

    /// <summary>Leave the checksums out of the manifests and the release list (a package without a checksum).</summary>
    public bool OmitDigests { get; set; }

    /// <summary>Every URL asked for, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>
    /// The pictures of the store: (app, file in docs/images) → the PNG, null = none (404). Not set: a plain sample picture
    /// (<see cref="SamplePicture"/>) for every light picture of the catalog, no dark ones.
    /// </summary>
    public Func<ThixApp, string, byte[]?>? Pictures { get; set; }

    /// <summary>The commits every repository's feed lists (newest first): the message and how many hours before <see cref="PublishedAt"/>.</summary>
    public List<(string Message, int HoursBefore)> Commits { get; } =
    [
        ("Install options (SETUP-08): the setup asks for the desktop shortcut, autostart and file types\n\nThe launcher passes them on.", 2),
        ("Settings: the window remembers its size", 26),
        ("Fix: the tray icon shows waiting updates again", 50),
    ];

    /// <summary>The block "installer" of an app's manifests (SETUP-08); an app without one publishes manifests without it (an older build).</summary>
    public Dictionary<string, InstallerConfig> Installers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The install options of the sample ThixNote: no desktop shortcut, no autostart, automatic updates, Markdown on, text off.</summary>
    public static InstallerConfig SampleNoteInstaller { get; } = new()
    {
        DesktopShortcut = new InstallerSwitch { Default = false },
        Autostart = new InstallerSwitch { Default = false },
        AutomaticUpdates = new InstallerSwitch { Default = true },
        FileTypes =
        [
            new InstallerFileType { Extension = ".md", Name = "Markdown-Notiz", Default = true },
            new InstallerFileType { Extension = ".txt", Name = "Textdatei", Default = false },
        ],
    };

    /// <summary>
    /// The sample state of the snapshots: ThixCalc 0.1.1, ThixClip 0.1.12 in release (installed: 0.1.9), ThixContacts 0.1.1, ThixFred 0.1.1, ThixMail 0.1.1, ThixNote 1.1.1, ThixShot 0.2.7, ThixShow 0.1.1, ThixTunnel 0.2.8, ThixWrite 0.1.1, nightlies of all;
    /// ThixNote with install options (<see cref="SampleNoteInstaller"/>), the others as builds without them.
    /// </summary>
    public static SampleGitHub WithSampleReleases()
    {
        var github = new SampleGitHub();
        github.Installers["thixnote"] = SampleNoteInstaller;
        github.Versions[("thixcalc", AppVersion.Release)] = "0.1.1-20261119";
        github.Versions[("thixcalc", AppVersion.Nightly)] = "nightly-3-20261119";
        github.Versions[("thixcalendar", AppVersion.Release)] = "0.1.3-20261117";
        github.Versions[("thixcalendar", AppVersion.Nightly)] = "nightly-4-20261117";
        github.Versions[("thixclip", AppVersion.Release)] = "0.1.12-20261115";
        github.Versions[("thixclip", AppVersion.Nightly)] = "nightly-14-20261116";
        github.Versions[("thixcontacts", AppVersion.Release)] = "0.1.1-20261122";
        github.Versions[("thixcontacts", AppVersion.Nightly)] = "nightly-2-20261122";
        github.Versions[("thixfred", AppVersion.Release)] = "0.1.1-20261117";
        github.Versions[("thixfred", AppVersion.Nightly)] = "nightly-2-20261117";
        github.Versions[("thixmail", AppVersion.Release)] = "0.1.1-20261122";
        github.Versions[("thixmail", AppVersion.Nightly)] = "nightly-3-20261122";
        github.Versions[("thixnote", AppVersion.Release)] = "1.1.1-20261116";
        github.Versions[("thixnote", AppVersion.Nightly)] = "nightly-5-20261116";
        github.Versions[("thixshot", AppVersion.Release)] = "0.2.7-20261122";
        github.Versions[("thixshot", AppVersion.Nightly)] = "nightly-8-20261122";
        github.Versions[("thixshow", AppVersion.Release)] = "0.1.1-20261119";
        github.Versions[("thixshow", AppVersion.Nightly)] = "nightly-2-20261119";
        github.Versions[("thixtunnel", AppVersion.Release)] = "0.2.8-20261117";
        github.Versions[("thixtunnel", AppVersion.Nightly)] = "nightly-7-20261117";
        github.Versions[("thixwrite", AppVersion.Release)] = "0.1.1-20261119";
        github.Versions[("thixwrite", AppVersion.Nightly)] = "nightly-12-20261119";
        return github;
    }

    /// <summary>The bytes of a sample package: the app and the version it installs (<see cref="Platform.SampleAppSystem"/> reads them).</summary>
    public static byte[] PackageBytes(string appId, string version) => Encoding.UTF8.GetBytes($"sample package {appId} {version}");

    /// <summary>The version a sample package installs, or null.</summary>
    public static (string AppId, string Version)? ReadPackage(string file)
    {
        var parts = File.ReadAllText(file, Encoding.UTF8).Split(' ');
        return parts is ["sample", "package", var appId, var version] ? (appId, version) : null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        lock (Requests)
        {
            Requests.Add(url);
        }

        if (FailWith is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }

        return Task.FromResult(Answer(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage? Answer(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath.Trim('/').Split('/');

        // Each store picture comes from the directory the app's repository publishes.
        if (uri.Host == "raw.githubusercontent.com" && path is [_, var pictureRepo, _, .., var picture])
        {
            var pictureApps = AppsOf(pictureRepo).Where(app => StoreService.PictureUrl(app, picture) == uri.AbsoluteUri).ToList();
            return PictureAnswer(request, pictureApps, picture);
        }

        // github.com/<owner>/<repo>/commits/<branch>.atom – the changes
        if (uri.Host == "github.com" && path is [_, var commitRepo, "commits", var feed] && feed.EndsWith(".atom", StringComparison.Ordinal) &&
            AppsOf(commitRepo) is [var commitApp, ..])
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CommitFeed(commitApp), Encoding.UTF8, "application/atom+xml") };
        }

        // api.github.com/repos/<owner>/<repo>/releases – only a repository of one app publishes single releases
        if (uri.Host == "api.github.com" && path is ["repos", _, var repoName, "releases"] && AppsOf(repoName) is [var listApp])
        {
            return Json(ReleaseList(listApp));
        }

        // github.com/<owner>/<repo>/releases/download/<tag>/<file>
        if (uri.Host != "github.com" || path is not [_, var repo, "releases", "download", var tag, var file])
        {
            return null;
        }

        // A repository may publish several apps (ThixOffice: ThixWrite, ThixCalc …): the file says which one is meant.
        var apps = AppsOf(repo);
        var channel = tag.EndsWith("-channel", StringComparison.Ordinal) ? tag[..^"-channel".Length] : null;
        if (file.EndsWith("-update.json", StringComparison.Ordinal))
        {
            var manifestApp = apps.FirstOrDefault(app => string.Equals(ManifestFileOf(app), file, StringComparison.Ordinal));
            return manifestApp is not null && channel is not null && Versions.TryGetValue((manifestApp.Id, channel), out var version)
                ? Json(Manifest(manifestApp, channel, version))
                : null;
        }

        foreach (var app in apps)
        {
            var version = channel is not null ? Versions.GetValueOrDefault((app.Id, channel)) : tag.TrimStart('v');
            if (version is not null && PackageNames(app, version).Contains(file))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PackageBytes(app.Id, version)) };
            }
        }

        return null;
    }

    /// <summary>The manifest a channel publishes for <paramref name="app"/>: its own name, or "&lt;id&gt;-update.json" (as <see cref="ReleaseFeed"/> asks for it).</summary>
    private static string ManifestFileOf(ThixApp app) => app.ManifestFileName ?? $"{app.Id}-update.json";

    /// <summary>The packages a channel manifest names, after the app like the real ones: win-x64 ThixWriteSetup.exe, osx-arm64 ThixWrite-macOS-arm64.zip, osx-x64 ….</summary>
    private static (string Platform, string File)[] ManifestPackages(ThixApp app) => app.Id switch
    {
        "thixshot" => [("win-x64", "ThixShot-win-x64.msi"), ("osx-arm64", "ThixShot-osx-arm64.zip"), ("osx-x64", "ThixShot-osx-x64.zip")],
        _ => [("win-x64", $"{app.Name}Setup.exe"), ("osx-arm64", $"{app.Name}-macOS-arm64.zip"), ("osx-x64", $"{app.Name}-macOS-x64.zip")],
    };

    /// <summary>The assets of a single release (<see cref="FeedKind.GitHubReleases"/>).</summary>
    private static string[] ReleaseAssets(ThixApp app, string version) =>
    [
        $"{app.Name}-Setup-{version}-win-x64.exe", $"{app.Name}-{version}-win-x64-portable.zip",
        $"{app.Name}-{version}-osx-arm64-portable.zip", $"{app.Name}-{version}-osx-arm64.dmg",
        $"{app.Name}-{version}-osx-x64-portable.zip",
    ];

    /// <summary>Every file name of a sample package of <paramref name="app"/>: the app is part of the name, so apps of one repository never share one.</summary>
    private static IEnumerable<string> PackageNames(ThixApp app, string version) =>
        ManifestPackages(app).Select(package => package.File).Concat(ReleaseAssets(app, version));

    private object Manifest(ThixApp app, string channel, string version)
    {
        var parsed = AppVersion.Parse(version)!;
        var sha = OmitDigests ? string.Empty : Sha(app.Id, version);
        var files = ManifestPackages(app);
        var packages = files.ToDictionary(package => package.Platform, package => new { file = package.File, sha256 = sha });
        if (app.Id == "thixshot")
        {
            return new { version, channel, buildNumber = parsed.BuildNumber, packages };
        }

        return new
        {
            version,
            channel,
            buildNumber = parsed.BuildNumber,
            setup = files[0].File,
            sha256 = sha,
            publishedAt = PublishedAt,
            packages,
            installer = Installers.GetValueOrDefault(app.Id),
        };
    }

    private object[] ReleaseList(ThixApp app)
    {
        var releases = new List<object>();
        foreach (var channel in new[] { AppVersion.Nightly, AppVersion.Release })
        {
            if (!Versions.TryGetValue((app.Id, channel), out var version))
            {
                continue;
            }

            var tag = channel == AppVersion.Release ? $"v{version}" : version;
            var assets = ReleaseAssets(app, version)
                .Select(name => (object)new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["size"] = 41_500_000L,
                    ["digest"] = OmitDigests ? null : $"sha256:{Sha(app.Id, version)}",
                    ["browser_download_url"] = $"https://github.com/{app.Repository}/releases/download/{tag}/{name}",
                })
                .ToArray();
            releases.Add(new Dictionary<string, object>
            {
                ["tag_name"] = tag,
                ["prerelease"] = channel == AppVersion.Nightly,
                ["draft"] = false,
                ["published_at"] = PublishedAt,
                ["html_url"] = $"https://github.com/{app.Repository}/releases/tag/{tag}",
                ["assets"] = assets,
            });
        }

        return [.. releases];
    }

    private HttpResponseMessage? PictureAnswer(HttpRequestMessage request, List<ThixApp> apps, string file)
    {
        byte[]? bytes = null;
        foreach (var app in apps)
        {
            bytes ??= Pictures is { } pictures
                ? pictures(app, file)
                : app.Screenshots.Any(picture => picture.File == file) ? SamplePicture.Png(app.Id + file) : null;
        }

        if (bytes is null)
        {
            return null;
        }

        var etag = $"\"{Convert.ToHexString(SHA256.HashData(bytes))[..16]}\"";
        if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag))
        {
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    /// <summary>A commit feed like GitHub's: the title cut, the whole message as HTML in the content.</summary>
    private string CommitFeed(ThixApp app)
    {
        var entries = Commits.Select((commit, index) =>
        {
            var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.Repository + index))).ToLowerInvariant()[..40];
            var title = commit.Message.Split('\n')[0];
            var html = System.Net.WebUtility.HtmlEncode($"<pre style='white-space:pre-wrap'>{System.Net.WebUtility.HtmlEncode(commit.Message)}</pre>");
            return $"""
                      <entry>
                        <id>tag:github.com,2008:Grit::Commit/{sha}</id>
                        <link type="text/html" rel="alternate" href="https://github.com/{app.Repository}/commit/{sha}"/>
                        <title>
                            {System.Net.WebUtility.HtmlEncode(title.Length > 70 ? title[..69] + "…" : title)}
                        </title>
                        <updated>{PublishedAt.AddHours(-commit.HoursBefore).UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}</updated>
                        <content type="html">{html}</content>
                      </entry>
                    """;
        });
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <feed xmlns="http://www.w3.org/2005/Atom" xml:lang="en-US">
                  <title>Recent Commits</title>
                {string.Join("\n", entries)}
                </feed>
                """;
    }

    private string Sha(string appId, string version) =>
        BreakChecksums ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(PackageBytes(appId, version))).ToLowerInvariant();

    /// <summary>Every app that publishes from the repository <paramref name="repositoryName"/>, ThixLauncher's own updates included.</summary>
    private List<ThixApp> AppsOf(string repositoryName) =>
        Apps.Append(Launcher).Where(app => app.Repository.EndsWith("/" + repositoryName, StringComparison.OrdinalIgnoreCase)).ToList();

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
