using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using MatCloud.Contract;
using MatDesktop;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using ThixLauncher.Core.Platform;

namespace ThixLauncher;

/// <summary>
/// "ThixLauncher --snapshot &lt;folder&gt;": draws the windows of ThixLauncher into PNG files without a screen (Avalonia
/// headless with Skia, the real windows, themes and fonts of the machine), in light and dark – the pictures of the README
/// and of MatDesign. It works in a temporary data folder (Program sets it) with sample data only: GitHub in memory
/// (<see cref="SampleGitHub"/>), a computer in memory with ThixClip 0.1.9 installed (<see cref="SampleAppSystem"/>), a pinned
/// clock, version 1.0.0 on the release channel and a fixed computer name, MatCloud in memory (<see cref="SampleCloud"/>). No app, registry value,
/// browser or network is touched.
/// </summary>
internal static class Snapshot
{
    public const string Argument = "--snapshot";

    public const string Version = "1.0.0";

    public const string Channel = "release";

    public const string MachineName = "ANNA-PC";

    /// <summary>"Now" in every picture (local time): Monday, 16 November 2026, 14:32.</summary>
    public static readonly DateTimeOffset Moment = new(new DateTime(2026, 11, 16, 14, 32, 0, DateTimeKind.Local));

    private const string ProgramFolder = @"C:\Users\Anna\AppData\Local\Programs\ThixLauncher";
    private const string DataFolder = @"C:\Users\Anna\AppData\Local\ThixLauncher";

    private static readonly string[] Themes = ["light", "dark"];

    private static readonly string[] ThemedPictures =
    [
        "setup-welcome", "setup-options", "setup-progress", "setup-finish", "setup-update", "setup-uninstall",
        "main-apps", "main-installing", "main-detail", "main-settings", "main-settings-cloud-code", "main-settings-cloud", "main-about", "main-folded", "dialog-uninstall", "toast",
    ];

    /// <summary>Every file a snapshot writes (the tray icons are the same in both themes).</summary>
    public static IReadOnlyList<string> FileNames { get; } =
    [
        .. ThemedPictures.SelectMany(name => Themes.Select(theme => $"{name}-{theme}.png")),
        "tray-icon-windows.png", "tray-icon-windows-updates.png", "tray-icon-macos.png", "tray-icon-macos-updates.png",
    ];

    /// <summary>The command: sets Avalonia up headless, draws every picture into the folder, ends with 0 (all written) or 1.</summary>
    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, Argument);
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Usage: ThixLauncher {Argument} <folder>");
            return 2;
        }

        var folder = Path.GetFullPath(args[index + 1]);
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        AvaloniaSynchronizationContext.InstallIfNeeded();

        int exitCode;
        try
        {
            var report = Render(folder);
            foreach (var file in report.Written)
            {
                Console.WriteLine($"written  {Path.Combine(folder, file)}");
            }

            foreach (var (file, reason) in report.Skipped)
            {
                Console.WriteLine($"skipped  {file}: {reason}");
            }

            exitCode = report.Written.Count + report.Skipped.Count == FileNames.Count ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Snapshot failed: {exception}");
            exitCode = 1;
        }

        try
        {
            Directory.Delete(AppPaths.DataDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return exitCode;
    }

    /// <summary>
    /// Draws every picture into <paramref name="folder"/> (Avalonia must run headless, the data folder must be a fresh one:
    /// the command and the UI tests take care of both). Existing files of the same names are replaced.
    /// </summary>
    public static SnapshotReport Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new SnapshotReport();
        var previousTheme = Application.Current!.RequestedThemeVariant;
        var system = new SampleAppSystem();
        using (Isolate(system))
        {
            foreach (var theme in Themes)
            {
                Application.Current.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                ResetSample(system);
                RenderSetup(folder, theme, report);
                RenderMainWindow(folder, theme, system, report);
                RenderToast(folder, theme, report);
            }

            RenderTrayIcons(folder, report);
        }

        Application.Current.RequestedThemeVariant = previousTheme;
        return report;
    }

    /// <summary>The sample state: ThixClip 0.1.9 installed (0.1.12 waits), ThixNote not installed, the feeds read.</summary>
    private static void ResetSample(SampleAppSystem system)
    {
        system.Installed.Clear();
        system.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        AppService.ResetForTests();
        Wait(AppService.RefreshAsync());
    }

    private static void RenderSetup(string folder, string theme, SnapshotReport report)
    {
        foreach (var page in Enum.GetValues<SetupSnapshotPage>())
        {
            var name = $"setup-{page.ToString().ToLowerInvariant()}";
            if (!OperatingSystem.IsWindows())
            {
                report.Skipped.Add(($"{name}-{theme}.png", "the setup assistant exists on Windows only (macOS: the app bundle and the dmg)"));
                continue;
            }

            RenderSetupPage(folder, $"{name}-{theme}.png", page, report);
        }
    }

    /// <summary>One page of the setup with fixed values: nothing is installed, removed or asked from the system.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RenderSetupPage(string folder, string file, SetupSnapshotPage page, SnapshotReport report)
    {
        var installed = page == SetupSnapshotPage.Update ? "0.9.0" : Version;
        var hold = new TaskCompletionSource();
        var window = Setup.ThixLauncherSetup.Create(page == SetupSnapshotPage.Uninstall, page == SetupSnapshotPage.Update, installed, ProgramFolder, async run =>
        {
            if (page != SetupSnapshotPage.Progress)
            {
                return;
            }

            run.Progress.Report(new MatSetupReport($"Installiere ThixLauncher nach {ProgramFolder}", MatSetupStepState.Done));
            run.Progress.Report(new MatSetupReport("Programmdatei kopiert", MatSetupStepState.Done));
            run.Progress.Report(new MatSetupReport("Startmenü-Eintrag angelegt", MatSetupStepState.Done));
            run.Progress.Report(new MatSetupReport("Autostart bei der Windows-Anmeldung eingerichtet", MatSetupStepState.Running, 65));
            await hold.Task;
        });
        window.Show();
        Settle(window);
        if (page == SetupSnapshotPage.Options)
        {
            window.GoTo(MatSetupStepKind.Options);
        }
        else if (page is SetupSnapshotPage.Progress or SetupSnapshotPage.Finish)
        {
            _ = window.RunAsync();
        }

        Settle(window);
        Save(window, folder, file, report);
        hold.TrySetResult();
        Settle(window);
        window.Close();
    }

    private static void RenderMainWindow(string folder, string theme, SampleAppSystem system, SnapshotReport report)
    {
        var window = new MainWindow();
        window.ShowWindow();
        window.Width = 1180;
        window.Height = 760;
        Settle(window, waitLonger: true);
        window.Apps.Select("thixclip");
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-apps-{theme}.png", report);

        // ThixNote while it installs: the progress of the download in its row.
        system.Hold = new TaskCompletionSource();
        var installing = AppService.InstallAsync("thixnote");
        Settle(window, waitLonger: true);
        window.Apps.Select("thixnote");
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-installing-{theme}.png", report);
        system.Hold.TrySetResult();
        Wait(installing);
        system.Hold = null;
        Settle(window);

        window.ShowDetails("thixclip");
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-detail-{theme}.png", report);

        window.ShowSettings(Pages.SettingsPage.AppsTab);
        Settle(window);
        Save(window, folder, $"main-settings-{theme}.png", report);

        // Einstellungen › Cloud: the code waiting for the browser, then signed in at the sample cloud.
        window.ShowSettings(Pages.SettingsPage.CloudTab);
        var settings = (Pages.SettingsPage)window.PageHost.Content!;
        settings.ServerUrlBox.Text = SampleCloud.Address;
        settings.ShowBrowserCode(new DeviceLinkStart(SampleCloud.Code, $"{SampleCloud.Address}{CloudContract.ConnectPage}?code={SampleCloud.Code}", "sample", 600, 2));
        Settle(window);
        Save(window, folder, $"main-settings-cloud-code-{theme}.png", report);
        settings.ShowSignInWay(password: false);
        Wait(CloudService.ConnectAsync(SampleCloud.Address, SampleCloud.UserName, SampleCloud.Password, MachineName));
        Settle(window);
        Save(window, folder, $"main-settings-cloud-{theme}.png", report);
        Wait(CloudService.DisconnectAsync());
        Settle(window);

        // The about page tells this machine's system and folder: the pictures show a typical installation instead.
        window.ShowAbout();
        var about = (Pages.AboutPage)window.PageHost.Content!;
        about.SystemText.Text = OperatingSystem.IsMacOS()
            ? $"macOS 15.5 ({UpdateService.PlatformKey}), .NET 10.0.0"
            : $"Microsoft Windows 10.0.26100 ({UpdateService.PlatformKey}), .NET 10.0.0";
        about.DataText.Text = OperatingSystem.IsMacOS() ? "/Users/anna/Library/Application Support/ThixLauncher" : DataFolder;
        Settle(window);
        Save(window, folder, $"main-about-{theme}.png", report);

        window.ShowApps(AppFilter.All, "thixclip");
        window.ToggleSidebar();
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-folded-{theme}.png", report);
        window.ToggleSidebar();
        Settle(window);

        // The question before removing an app.
        _ = AppActions.RunAsync(AppAction.Uninstall, AppService.Get("thixclip"), window);
        Settle(window);
        var dialog = window.OwnedWindows.OfType<Dialogs.ConfirmDialog>().Single();
        Settle(dialog);
        Save(dialog, folder, $"dialog-uninstall-{theme}.png", report);
        dialog.Close();
        Settle(window);

        window.IsExiting = true;
        window.Close();
    }

    private static void RenderToast(string folder, string theme, SnapshotReport report)
    {
        var toast = MatNotification.Show("Update verfügbar", "ThixClip 0.1.12-20261115 – im Launcher installieren.");
        Settle(toast);
        Save(toast, folder, $"toast-{theme}.png", report);
        toast.Close();
        Settle(toast);
    }

    /// <summary>The tray / menu bar icons at 64 px as MatDesktop draws them (TRAY-02): running green, updates waiting amber.</summary>
    private static void RenderTrayIcons(string folder, SnapshotReport report)
    {
        foreach (var (template, platform) in new[] { (false, "windows"), (true, "macos") })
        {
            foreach (var updates in new[] { 0, 1 })
            {
                var name = updates > 0 ? $"tray-icon-{platform}-updates.png" : $"tray-icon-{platform}.png";
                using var mark = Tray.TrayIcon.Mark(template);
                File.WriteAllBytes(Path.Combine(folder, name), MatTrayIcons.Png(mark, Tray.TrayIcon.StateFor(updates), template));
                report.Written.Add(name);
            }
        }
    }

    /// <summary>Waits for work of the business layer while the dispatcher runs (its continuations may need the UI thread).</summary>
    private static void Wait(Task task)
    {
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>Lets the dispatcher, the layout and the render timer run; longer for images and animations.</summary>
    private static void Settle(TopLevel topLevel, bool waitLonger = false)
    {
        for (var round = 0; round < (waitLonger ? 40 : 8); round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(waitLonger ? 40 : 15);
        }

        Dispatcher.UIThread.RunJobs();
        topLevel.UpdateLayout();
    }

    private static void Save(TopLevel topLevel, string folder, string name, SnapshotReport report)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = topLevel.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{name}: no frame was rendered");
        frame.Save(Path.Combine(folder, name));
        report.Written.Add(name);
    }

    /// <summary>Pins clock, version and computer name, and puts GitHub, the computer, policies and autostart in memory.</summary>
    private static IDisposable Isolate(SampleAppSystem system)
    {
        AppClock.Pin(Moment);
        AppInfo.Pin(Version, Channel, buildNumber: 0, MachineName);
        AutostartService.UseInMemoryEntry(true);
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        AppSystem.UseForTests(system);
        var github = SampleGitHub.WithSampleReleases();
        github.PublishedAt = Moment.AddDays(-1);
        ReleaseFeed.HandlerForTests = github;
        var cloud = new SampleCloud();
        CloudService.HandlerForTests = () => cloud;
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(true);
        CloudHandoff.Folder = Path.Combine(AppPaths.DataDirectory, "handoff");
        Ui.OpenExternalForTests = _ => { };
        return new Restore(() =>
        {
            Ui.OpenExternalForTests = null;
            CloudHandoff.Folder = CloudHandoff.DefaultFolder();
            TokenStore.UseMemoryForTests(false);
            CloudService.ResetForTests();
            CloudService.HandlerForTests = null;
            ReleaseFeed.HandlerForTests = null;
            AppSystem.UseForTests(null);
            AppService.ResetForTests();
            AccessPolicy.UsePolicyReaderForTests(null);
            AutostartService.UseInMemoryEntry(null);
            AppInfo.Pin(null);
            AppClock.Pin(null);
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

/// <summary>What a snapshot wrote (file names) and what it left out, with the reason.</summary>
internal sealed class SnapshotReport
{
    public List<string> Written { get; } = [];

    public List<(string File, string Reason)> Skipped { get; } = [];
}

/// <summary>The pages of the setup that "ThixLauncher --snapshot" draws (setup-welcome ... setup-uninstall).</summary>
internal enum SetupSnapshotPage
{
    Welcome,
    Options,
    Progress,
    Finish,
    Update,
    Uninstall,
}
