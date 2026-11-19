using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using Avalonia.Threading;
using MatCloud.Contract;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using ThixLauncher.Core.Platform;

namespace ThixLauncher;

/// <summary>
/// "ThixLauncher.exe --self-test &lt;report file&gt;": the published program checks itself – everything trimming or
/// single-file packaging could break silently: JSON of settings, the manifests and the GitHub release list, the version
/// rules, the whole way of an installation (feed, download, SHA-256, install, start, remove) against GitHub and a computer
/// in memory, the sign-in at MatCloud and its hand-over to an installed app (a cloud in memory, the token protected with DPAPI on Windows), Windows shortcuts (COM) and the windows of the app with their bindings. It runs in a fresh temporary data
/// folder (Program sets it), never touches the user's data or apps, writes a report and ends with exit code 0 (all passed)
/// or 1. CI runs it on every Windows build.
/// </summary>
internal static class SelfTest
{
    public const string Argument = "--self-test";

    public static async Task<int> RunAsync(string reportPath)
    {
        var report = new StringBuilder($"ThixLauncher {AppInfo.Version} self test, data folder {AppPaths.DataDirectory}{Environment.NewLine}");
        var failures = 0;

        async Task Check(string name, Func<Task<string>> check)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var note = await check();
                report.AppendLine($"ok    {name} ({watch.ElapsedMilliseconds} ms){(note.Length > 0 ? " – " + note : "")}");
            }
            catch (Exception exception)
            {
                failures++;
                report.AppendLine($"FAIL  {name}: {exception}");
            }
        }

        var system = new SampleAppSystem();
        var github = SampleGitHub.WithSampleReleases();
        AppSystem.UseForTests(system);
        ReleaseFeed.HandlerForTests = github;
        AutostartService.UseInMemoryEntry(false);
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        var cloud = new SampleCloud();
        CloudService.HandlerForTests = () => cloud;
        TokenStore.UseMemoryForTests(!OperatingSystem.IsWindows());
        CloudHandoff.Folder = Path.Combine(AppPaths.DataDirectory, "handoff");

        await Check("settings (JSON)", () =>
        {
            SettingsService.Update(settings => settings with { AppChannel = AppChannel.Nightly, Theme = ThemeChoice.Dark });
            SettingsStore.Reset();
            var read = SettingsService.Get();
            Ensure(read is { AppChannel: AppChannel.Nightly, Theme: ThemeChoice.Dark }, "settings did not survive a save");
            SettingsService.Update(settings => settings with { AppChannel = AppChannel.Release, Theme = ThemeChoice.System });
            return Task.FromResult("");
        });

        await Check("update manifest (JSON)", async () =>
        {
            const string json = """{"version":"0.1.9-20261115","channel":"release","buildNumber":9,"setup":"ThixLauncherSetup.exe","sha256":"AB","packages":{"win-x64":{"file":"ThixLauncherSetup.exe","sha256":"AB"}}}""";
            var manifest = await new StringContent(json, Encoding.UTF8, "application/json").ReadFromJsonAsync<UpdateManifest>();
            Ensure(manifest is { BuildNumber: 9 } && manifest.PackageFor("win-x64")?.File == "ThixLauncherSetup.exe", "manifest not read");
            return "";
        });

        await Check("install options (installer.json, arguments)", () =>
        {
            Ensure(InstallerOptions.Config is { Autostart: not null, DesktopShortcut: not null, AutomaticUpdates: not null }, "installer.json not read");
            var request = InstallerOptions.Parse([InstallerOptions.QuietArgument, "--autostart=no", "--desktop-shortcut=yes"]);
            Ensure(request is { Action: SetupAction.Install, Choices: { Autostart: false, DesktopShortcut: true } }, "arguments not read");
            return Task.FromResult("");
        });

        await Check("install options of an app (manifest block, setup arguments)", async () =>
        {
            const string json = """{"version":"1.1.1-20261116","channel":"release","buildNumber":1,"setup":"ThixNoteSetup.exe","sha256":"AB","installer":{"autostart":{"default":false},"fileTypes":[{"extension":".md","name":"Markdown-Notiz","default":true}]}}""";
            var manifest = await new StringContent(json, Encoding.UTF8, "application/json").ReadFromJsonAsync<UpdateManifest>();
            Ensure(manifest?.Installer is { Autostart.Default: false, FileTypes: [{ Extension: ".md", Default: true }] }, "the block \"installer\" was not read");
            var setup = AppInstallOptions.SetupFor(AppCatalog.Find("thixnote")!, manifest!.Installer, new InstallChoices(Autostart: true), isUpdate: false);
            Ensure(setup.WindowsArguments.SequenceEqual(["--quiet", "--autostart=yes", "--file-types=.md", "--start"]), string.Join(' ', setup.WindowsArguments));
            return "";
        });

        await Check("versions", () =>
        {
            Ensure(AppVersion.Parse("0.1.12-20261115") is { Channel: "release", BuildNumber: 12 }, "release version");
            Ensure(AppVersion.Parse("nightly-43-20261116") is { Channel: "nightly", BuildNumber: 43 }, "nightly version");
            Ensure(AppVersion.Parse("v1.0.2-20261116")!.IsNewerThan(AppVersion.Parse("1.0.1-20261116")!), "comparison");
            return Task.FromResult("");
        });

        await Check("feeds (manifest and GitHub API)", async () =>
        {
            await AppService.RefreshAsync();
            var clip = AppService.Get("thixclip");
            var note = AppService.Get("thixnote");
            Ensure(clip.Latest?.Version.Text == "0.1.12-20261115" && clip.Latest.Sha256?.Length == 64, $"ThixClip: {clip.Latest?.Version.Text} {clip.FeedError}");
            Ensure(note.Latest?.Version.Text == "1.1.1-20261116" && note.Latest.Sha256?.Length == 64, $"ThixNote: {note.Latest?.Version.Text} {note.FeedError}");
            return $"{github.Requests.Count} requests";
        });

        await Check("install, start, remove (download, SHA-256)", async () =>
        {
            await AppService.InstallAsync("thixnote");
            Ensure(AppService.Get("thixnote").Installed?.VersionText == "1.1.1-20261116", "not installed");
            Ensure(system.Setups[^1].Setup.WindowsArguments is ["--quiet", ..], "the install options of the manifest were not passed");
            AppService.Launch("thixnote");
            Ensure(system.Launched.SequenceEqual(["thixnote"]), "not started");
            await AppService.UninstallAsync("thixnote");
            Ensure(!AppService.Get("thixnote").IsInstalled, "not removed");
            return "";
        });

        await Check("MatCloud: sign-in and hand-over to an installed app", async () =>
        {
            await CloudService.ConnectAsync(SampleCloud.Address, SampleCloud.UserName, SampleCloud.Password, "SELFTEST");
            Ensure(CloudService.IsConnected, "not signed in");
            var account = await AppService.InstallAsync("thixclip");
            Ensure(account == "Anna Beispiel (anna)", $"no hand-over ({account})");
            var handoff = CloudHandoff.Find(CloudApps.Clip) ?? throw new InvalidOperationException("the hand-over file is missing");
            using var app = new MatCloudClient(handoff.ServerUrl, null, "ThixClip/selftest", cloud);
            Ensure((await app.RedeemHandoffAsync(handoff, CancellationToken.None))?.Account.App == CloudApps.Clip, "the hand-over gives no sign-in");
            await AppService.UninstallAsync("thixclip");
            await CloudService.DisconnectAsync();
            Ensure(!CloudService.IsConnected && cloud.Devices.Count == 1, "not signed out");
            return "";
        });

        if (OperatingSystem.IsWindows())
        {
            await Check("shortcut (COM)", () =>
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var link = Path.Combine(AppPaths.DataDirectory, "ThixLauncher.lnk");
                Core.Platform.Windows.ShellLink.Write(link, App.ExecutablePath, AppPaths.DataDirectory, "Selbsttest", App.ExecutablePath);
                Ensure(File.Exists(link) && new FileInfo(link).Length > 0, "no .lnk written");
                return Task.FromResult("");
            });
        }

        await Check("windows and bindings", async () =>
        {
            system.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
            AppService.DetectInstalled();
            var window = new MainWindow();
            window.ShowWindow();
            await Settle();
            Ensure(window.Apps.Rows.Count == AppCatalog.Apps.Count, "the list does not show every app of the catalog");
            Ensure(window.Apps.Rows.Single(row => row.App.Id == "thixclip").ActionText == "Aktualisieren", "the update is not offered");
            Ensure(window.QuickLaunchButtons.Count == 1, "the installed app is not in the title bar");
            window.ShowDetails("thixclip");
            await Settle();
            window.ShowSettings();
            await Settle();
            window.ShowAbout();
            await Settle();
            if (OperatingSystem.IsWindows())
            {
                var setup = Setup.ThixLauncherSetup.Create(isUninstall: false, InstallService.IsInstalled, InstallService.InstalledVersion, InstallService.ProgramFolder, _ => Task.CompletedTask);
                setup.Show();
                await Settle();
                setup.Close();
            }

            ProblemReports.WriteHang(TimeSpan.FromSeconds(9), withDump: false);
            Dialogs.ProblemReportDialog.ShowUnseen()?.Close();
            window.IsExiting = true;
            window.Close();
            return "";
        });

        report.AppendLine(failures == 0 ? "PASSED" : $"FAILED ({failures})");
        await File.WriteAllTextAsync(reportPath, report.ToString(), Encoding.UTF8);

        // The temporary data folder goes again (unless the report was written into it).
        if (!Path.GetFullPath(reportPath).StartsWith(Path.GetFullPath(AppPaths.DataDirectory), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.Delete(AppPaths.DataDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task Settle()
    {
        for (var round = 0; round < 10; round++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(30);
        }
    }
}
