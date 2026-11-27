using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MatDesktop;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using TrayIcon = ThixLauncher.Tray.TrayIcon;

namespace ThixLauncher;

/// <summary>
/// Start-up and end of ThixLauncher.
///  - Windows: the published ThixLauncher.exe is also its setup (RunSetupIfRequested).
///  - The look is MatDesktop's theme with ThixLauncher's accent (DESK-02).
///  - One instance per user and data folder (MatSingleInstance, DESK-05): a second start opens the window of the first one.
///  - "--autostart" (sign-in) and "--tray" (after an update) start hidden in the tray / menu bar - after an update asked for in the
///    window (or with the window open) the window shows again (UpdateService.TakeShowWindowAfterUpdate).
///  - The apps: their state is read at start and every 6 hours (AppCoordinator), the launcher itself looks for its own
///    updates (UpdateCoordinator).
/// </summary>
public partial class App : Application
{
    public const string TrayArgument = "--tray";

    /// <summary>ThixLauncher's accent (IDENT-01), the blue of the Thix family: MatDesktop derives every accent token from it.</summary>
    public const string Accent = "#2F6FED";

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MatSingleInstance? _instance;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private bool _isExiting;

    /// <summary>The program file; the autostart entry points to it.</summary>
    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ThixLauncher");

    public static App Instance => (App)Current!;

    /// <summary>The main window (null during setup).</summary>
    public MainWindow? MainWindow => _mainWindow;

    /// <summary>The main window is on the screen (not only in the tray).</summary>
    public bool IsMainWindowShown => _mainWindow is { IsVisible: true };

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // ThixLauncher speaks German, the texts of MatDesktop (title bar, menu, tray, setup) too.
        MatText.Language = "de";
        MatTheme.Install(this, new MatThemeOptions(Accent) { Icon = Ui.AppIcon });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        _desktop = desktop;
        var args = desktop.Args ?? [];

        var selfTest = Array.IndexOf(args, SelfTest.Argument);
        if (selfTest >= 0)
        {
            var report = selfTest + 1 < args.Length ? args[selfTest + 1] : Path.Combine(AppPaths.DataDirectory, "self-test.txt");
            Dispatcher.UIThread.Post(async () => EndProcess(await SelfTest.RunAsync(report)));
            return;
        }

        if (RunSetupIfRequested(args))
        {
            return;
        }

        if (!TakeSingleInstance())
        {
            EndProcess(0);
            return;
        }

        Start(args);
    }

    /// <summary>Opens the main window (tray, second start, Dock) and optionally navigates.</summary>
    public void ShowMainWindow(Action<MainWindow>? navigate = null)
    {
        if (_mainWindow is null || _isExiting)
        {
            return;
        }

        _mainWindow.ShowWindow();
        navigate?.Invoke(_mainWindow);
    }

    /// <summary>A notification of ThixLauncher's own (TRAY-04: only for what the user did not cause); a click opens the window.</summary>
    public void Notify(string title, string text, bool isWarning = false, Action<MainWindow>? navigate = null) =>
        MatNotification.Show(title, text, isWarning, () => ShowMainWindow(navigate));

    /// <summary>The setting "Erscheinungsbild": Wie das System, Hell, Dunkel (DESK-02).</summary>
    public static void ApplyTheme(ThemeChoice theme) => MatTheme.SetAppearance(theme switch
    {
        ThemeChoice.Light => MatAppearance.Light,
        ThemeChoice.Dark => MatAppearance.Dark,
        _ => MatAppearance.System,
    });

    /// <summary>"Beenden": ends the application (an app that is being installed goes on by itself: its setup is its own process).</summary>
    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        UiWatchdog.Stop();
        Log.Info("ThixLauncher exiting");
        AppCoordinator.Stop();
        _trayIcon?.Dispose();
        if (_mainWindow is not null)
        {
            _mainWindow.IsExiting = true;
            _mainWindow.Close();
        }

        _instance?.Dispose();
        _desktop?.Shutdown(0);
    }

    /// <summary>
    /// Ends the process through the main loop. Shutting down straight from OnFrameworkInitializationCompleted (update,
    /// quiet uninstall, second instance) throws "Dispatcher shut down" because the loop has not started yet.
    /// </summary>
    private void EndProcess(int exitCode) => Dispatcher.UIThread.Post(() => _desktop?.Shutdown(exitCode));

    private void Start(string[] args)
    {
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        UiWatchdog.Start();
        Log.Info($"ThixLauncher {AppInfo.Version} starting on {UpdateService.PlatformKey} ({string.Join(' ', args)})");

        ApplyTheme(SettingsService.Get().Theme);
        SettingsService.Changed += (_, settings) => Ui.Post(() => ApplyTheme(settings.Theme));

        _mainWindow = new MainWindow();
        _trayIcon = new TrayIcon(this);

        // After an update asked for in the window (or with the window open) the window comes back, even though the restart says "--tray".
        var startHidden = (args.Contains(TrayArgument) || args.Contains(AutostartService.StartupArgument)) && !UpdateService.TakeShowWindowAfterUpdate();
        if (!startHidden)
        {
            _mainWindow.ShowWindow();
        }

        // Crashed or hung last time: say so, with the report.
        Dispatcher.UIThread.Post(() => Dialogs.ProblemReportDialog.ShowUnseen(), DispatcherPriority.Background);

        // macOS: opening the app again (Finder, Spotlight, the Dock) shows the window.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, activation) =>
            {
                if (activation.Kind == ActivationKind.Reopen)
                {
                    ShowMainWindow();
                }
            };
        }

        // The apps: what is installed and what their channel offers; updates are offered or (setting) installed.
        AppCoordinator.UpdatesFound += (_, updates) =>
        {
            var names = string.Join(", ", updates.Select(status => $"{status.App.Name} {status.Latest?.Version.Text}"));
            Notify(updates.Count == 1 ? "Update verfügbar" : "Updates verfügbar", $"{names} – im Launcher installieren.",
                navigate: window => window.ShowApps(AppFilter.Updates));
        };
        AppCoordinator.AppsUpdated += (_, report) =>
        {
            if (report.Updated.Count > 0)
            {
                Notify("Apps aktualisiert", $"{string.Join(", ", report.Updated)} wurde automatisch aktualisiert.");
            }

            foreach (var (app, reason) in report.Failed)
            {
                Notify($"{app} nicht aktualisiert", reason, isWarning: true);
            }
        };
        AppCoordinator.Start();

        // The launcher itself (OTA, full auto update optional).
        UpdateCoordinator.Checked += (_, result) =>
        {
            _trayIcon?.SetUpdateAvailable(result.IsUpdateAvailable);
            if (result is { IsUpdateAvailable: true, Manifest: { } manifest } && !SettingsService.Get().InstallUpdatesAutomatically)
            {
                Notify("ThixLauncher-Update verfügbar", $"Version {manifest.Version} kann installiert werden (Einstellungen › Updates).",
                    navigate: window => window.ShowSettings(Pages.SettingsPage.UpdatesTab));
            }
        };
        UpdateCoordinator.Start();
    }

    /// <summary>
    /// The command line of SETUP-05 with the install options of SETUP-08 (InstallerOptions; a wrong value: exit code 2, the
    /// reason on stderr and in the log, nothing installed).
    /// Windows: the published ThixLauncher.exe is its own setup (InstallService):
    ///  - "--uninstall" (from "Installierte Apps"): the uninstall assistant, or silently with "--quiet".
    ///  - "--quiet [options]" (ThixInstall, an administrator): installs without a window; "--start" starts it in the tray.
    ///  - "--update [options]" (downloaded by the update check): installs itself silently and starts the installed ThixLauncher in the tray.
    ///  - started anywhere else than the program folder (e.g. from Downloads): the setup assistant.
    /// Both systems: "--configure [options]" applies the options to the installed ThixLauncher and ends. A development build
    /// (not single-file) runs as the application otherwise.
    /// </summary>
    private bool RunSetupIfRequested(string[] args)
    {
        SetupRequest request;
        try
        {
            request = InstallerOptions.Parse(args);
        }
        catch (BusinessException exception)
        {
            Log.Warning($"Setup arguments refused: {exception.Message}");
            Console.Error.WriteLine(exception.Message);
            EndProcess(2);
            return true;
        }

        if (request.Action == SetupAction.Configure)
        {
            var executable = OperatingSystem.IsWindows() && File.Exists(InstallService.InstalledExecutable) ? InstallService.InstalledExecutable : ExecutablePath;
            InstallerOptions.Apply(request.Choices, executable, Log.Info);
            EndProcess(0);
            return true;
        }

        return OperatingSystem.IsWindows() && RunWindowsSetup(request);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private bool RunWindowsSetup(SetupRequest request)
    {
        if (request.Action == SetupAction.Uninstall)
        {
            if (request.Quiet)
            {
                if (InstallService.Uninstall(deleteUserData: false, Log.Info))
                {
                    InstallService.ScheduleProgramFolderRemoval();
                }

                EndProcess(0);
                return true;
            }

            Setup.ThixLauncherSetup.Run(isUninstall: true, removeProgramFolder =>
            {
                if (removeProgramFolder)
                {
                    InstallService.ScheduleProgramFolderRemoval();
                }

                EndProcess(0);
            });
            return true;
        }

        if (request.Action is SetupAction.Install or SetupAction.Update)
        {
            var isUpdate = request.Action == SetupAction.Update;
            var exitCode = 0;
            try
            {
                InstallService.Install(ExecutablePath, request.Choices, Log.Info);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                                  or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
            {
                Log.Error(isUpdate ? "Update failed" : "Quiet install failed", exception);
                Console.Error.WriteLine(exception.Message);
                exitCode = 1;
            }

            // an update starts ThixLauncher again; a quiet install only with "--start"
            if ((isUpdate || request.Start) && File.Exists(InstallService.InstalledExecutable))
            {
                InstallService.StartInstalled(TrayArgument);
            }

            EndProcess(exitCode);
            return true;
        }

#if SINGLE_FILE
        const bool isPublishedBuild = true;
#else
        const bool isPublishedBuild = false;
#endif
        if (isPublishedBuild && !InstallService.IsInstalledExecutable(ExecutablePath))
        {
            Setup.ThixLauncherSetup.Run(isUninstall: false, _ => EndProcess(0));
            return true;
        }

        return false;
    }

    /// <summary>
    /// One instance per user and data folder (MatSingleInstance, DESK-05): a second start hands its arguments to the first
    /// one, which shows its window, and ends. A development run with its own THIXLAUNCHER_DATA_DIR does not meet the
    /// installed one. On Windows the setup may also ask the running instance to end (ListenForExitSignal).
    /// </summary>
    private bool TakeSingleInstance()
    {
        _instance = new MatSingleInstance(AppInfo.ProductName, AppPaths.HasOverride ? AppPaths.DataDirectory : null);
        _instance.SecondStart += (_, _) => Ui.Post(() => ShowMainWindow());
        if (!_instance.TryClaim(_desktop?.Args ?? []))
        {
            Log.Info("ThixLauncher runs already: its window was asked to show");
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            ListenForExitSignal();
        }

        return true;
    }

    /// <summary>The setup asks a running ThixLauncher to end (update, uninstall).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ListenForExitSignal()
    {
        if (AppPaths.HasOverride)
        {
            return; // a development run with its own data folder is none of the setup's business
        }

        var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstallService.ExitSignalName);
        var listener = new Thread(() =>
        {
            exitSignal.WaitOne();
            Ui.Post(() =>
            {
                Log.Info("Setup asked ThixLauncher to end");
                ExitApplication();
            });
        })
        {
            IsBackground = true,
            Name = "ThixLauncher exit signal listener",
        };
        listener.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled error", e.Exception);
        e.Handled = true;
        if (e.Exception is BusinessException business)
        {
            Notify("ThixLauncher", business.Message, isWarning: true);
            return;
        }

        Notify("Unerwarteter Fehler", $"{e.Exception.Message} – Details im Protokoll ({AppPaths.LogDirectory}).", isWarning: true);
    }
}
