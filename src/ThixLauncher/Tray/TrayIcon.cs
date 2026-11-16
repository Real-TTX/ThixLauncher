using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MatDesktop;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tray;

/// <summary>
/// The icon in the notification area (Windows) or the menu bar (macOS), with MatDesktop's tray (TRAY-01 … TRAY-04): the
/// menu in the order of every Mat tray – "ThixLauncher 1.0.0", the state in words, "ThixLauncher öffnen", ThixLauncher's own
/// items (the installed apps to start, "Nach Updates suchen", "Alle Apps aktualisieren" while updates wait),
/// "Einstellungen …", "Log-Ordner öffnen", "Update installieren …" while one of ThixLauncher waits, "Beenden". The state
/// is the dot of the icon: running green, updates of apps amber. Windows: a left click opens the window, the right button
/// the menu; macOS: every click opens the menu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly MatTrayMenu _menu;
    private readonly MatTray _tray;
    private bool _updateAvailable;
    private bool _refreshPosted;

    public TrayIcon(App app)
    {
        _app = app;
        _menu = new MatTrayMenu(AppInfo.ProductName, AppInfo.Version, () => _app.ExitApplication())
        {
            Open = () => _app.ShowMainWindow(),
            Settings = () => _app.ShowMainWindow(window => window.ShowSettings()),
            LogFolder = () =>
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                Ui.OpenExternal(AppPaths.LogDirectory);
            },
        };
        Update();
        _tray = new MatTray(app, _menu, Mark());
        _tray.MenuOpening += (_, _) => Update(); // macOS and Linux say when the menu opens
        AppService.Changed += OnChanged;
        SettingsService.Changed += OnChanged;
    }

    /// <summary>The model of the menu (MatTray builds the menu from it).</summary>
    public MatTrayMenu Menu => _menu;

    /// <summary>The state of the tray: running, or updates of apps waiting (amber, TRAY-02) with their number in words.</summary>
    public static MatState StateFor(int updates) => updates > 0
        ? MatState.Custom(updates == 1 ? "1 Update verfügbar" : $"{updates} Updates verfügbar", "--mat-warning", MatStateMark.Ring)
        : MatState.Running;

    /// <summary>The mark the tray icons are drawn from: the logo on Windows, the template image (black on transparent) on macOS.</summary>
    public static Bitmap Mark(bool? template = null) =>
        new(AssetLoader.Open(new Uri((template ?? OperatingSystem.IsMacOS()) ? "avares://ThixLauncher/Assets/tray-macos.png" : "avares://ThixLauncher/Assets/logo-64.png")));

    public void SetUpdateAvailable(bool available)
    {
        _updateAvailable = available;
        Refresh();
    }

    public void Dispose()
    {
        AppService.Changed -= OnChanged;
        SettingsService.Changed -= OnChanged;
        _tray.Dispose();
    }

    /// <summary>
    /// The state of the apps changed: the menu, the tooltip and (only when the state looks different) the icon follow.
    /// Windows does not say when the tray menu opens, so the model is kept up to date (a burst of changes is one refresh).
    /// </summary>
    private void OnChanged(object? sender, object? e)
    {
        if (_refreshPosted)
        {
            return;
        }

        _refreshPosted = true;
        Ui.Post(() =>
        {
            _refreshPosted = false;
            Refresh();
        });
    }

    private void Refresh()
    {
        Update();
        _tray.Refresh();
    }

    /// <summary>The model: the state, the apps to start, the update actions and whether an update of ThixLauncher waits.</summary>
    private void Update()
    {
        var all = AppService.All();
        var updates = all.Count(status => status.HasUpdate);
        _menu.State = StateFor(updates);
        _menu.TooltipText = all.Count(status => status.IsInstalled) is var installed && installed > 0
            ? $"{installed} {(installed == 1 ? "App" : "Apps")} installiert"
            : "Noch keine App installiert";
        _menu.InstallUpdate = _updateAvailable
            ? () => _app.ShowMainWindow(window => window.ShowSettings(Pages.SettingsPage.UpdatesTab))
            : null;
        _menu.ClearOwn();

        var launchable = all.Where(status => status.IsInstalled).ToList();
        if (launchable.Count > 0)
        {
            foreach (var status in launchable)
            {
                var id = status.App.Id;
                _menu.AddOwn($"{status.App.Name} starten", () => Launch(id), status.CanLaunch);
            }

            _menu.AddOwnSeparator();
        }

        _menu.AddOwn("Nach Updates suchen", () => _ = AppCoordinator.RefreshAsync(automatic: false), !AppService.IsRefreshing);
        if (updates > 0 && !AccessPolicy.AreAppChangesDisabled)
        {
            _menu.AddOwn(updates == 1 ? "Update installieren (1 App) …" : $"Alle Apps aktualisieren ({updates}) …",
                () => _app.ShowMainWindow(window => window.ShowApps(AppFilter.Updates)));
        }
    }

    private void Launch(string appId)
    {
        try
        {
            AppService.Launch(appId);
        }
        catch (BusinessException exception)
        {
            _app.Notify("Start nicht möglich", exception.Message, isWarning: true);
        }
    }
}
