using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MatDesktop;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using ThixLauncher.Pages;

namespace ThixLauncher;

/// <summary>
/// The main window: MatDesktop's chrome (MatTitleBar, DESK-03; MatNav, DESK-04, folds to a rail of 68 px) around the pages
/// (the library = the installed apps, the store = every app, the page of one app, settings, about). The installed apps sit in
/// the title bar to start them with one click. Keyboard: Ctrl/Cmd+1 library, +2 store, +3 updates, +F search, +R or F5 look
/// for updates, +, settings, +B fold the menu, Enter the page of the chosen app, Esc back (from an app's page to where it was
/// opened, from any other page to the library) or hide the window.
/// Close, Esc and Alt+F4 hide the window; only "Beenden" ends ThixLauncher (DESK-06).
/// </summary>
public partial class MainWindow : MatWindow
{
    private readonly AppsPage _apps = new();
    private readonly StorePage _store = new();
    private readonly Lazy<AppDetailPage> _detail;
    private readonly Lazy<SettingsPage> _settings = new(() => new SettingsPage());
    private readonly Lazy<AboutPage> _about = new(() => new AboutPage());
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly MatNavItem _navLibrary;
    private readonly MatNavItem _navStore;
    private readonly MatNavItem _navUpdates;
    private readonly MatNavItem _navSettings;
    private bool _refreshPosted;
    private bool _detailFromStore;

    public MainWindow()
    {
        InitializeComponent();
        UseOwnTitleBar();
        var settings = SettingsService.Get();
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;

        TitleBar.Logo = Ui.Logo;
        TitleBar.CloseRequested += (_, e) =>
        {
            e.Handled = true;
            Hide();
        };

        var command = Ui.CommandKey.Name;
        _navLibrary = new MatNavItem("apps", "Bibliothek", $"{command}+1") { IsCurrent = true };
        _navStore = new MatNavItem("store", "Store", $"{command}+2");
        _navUpdates = new MatNavItem("download", "Updates", $"{command}+3");
        _navSettings = new MatNavItem("settings", "Einstellungen", $"{command}+,");
        Sidebar.Items.AddRange([_navLibrary, _navStore, _navUpdates]);
        Sidebar.Settings = _navSettings;
        Sidebar.Version = AppInfo.Version;
        Sidebar.Channel = AppInfo.Channel;
        Sidebar.TitleBar = TitleBar;
        Sidebar.IsFolded = settings.IsSidebarCollapsed;
        Sidebar.FoldShortcut = $"{command}+B";
        Sidebar.ItemInvoked += (_, item) => OnNavInvoked(item);
        Sidebar.AboutRequested += (_, _) => ShowAbout();
        Sidebar.UpdateRequested += (_, _) => ShowSettings(SettingsPage.UpdatesTab);
        Sidebar.FoldChanged += (_, folded) => SettingsService.Remember(current => current with { IsSidebarCollapsed = folded });

        _detail = new Lazy<AppDetailPage>(() =>
        {
            var page = new AppDetailPage();
            page.BackRequested += (_, _) => GoBackFromDetails();
            return page;
        });
        _apps.DetailsRequested += (_, appId) => ShowDetails(appId);
        _apps.StoreRequested += (_, _) => ShowStore();
        _store.DetailsRequested += (_, appId) => ShowDetails(appId);
        _apps.FilterChanged += (_, filter) => Sidebar.Select(NavItemOf(filter));
        PageHost.Content = _apps;

        _sizeTimer.Tick += (_, _) =>
        {
            _sizeTimer.Stop();
            if (WindowState == WindowState.Normal)
            {
                SettingsService.Remember(current => current with { WindowWidth = Width, WindowHeight = Height });
            }
        };
        Resized += (_, _) =>
        {
            _sizeTimer.Stop();
            _sizeTimer.Start();
        };

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AppService.Changed += (_, _) => PostRefresh();
        UpdateCoordinator.Checked += (_, _) => Ui.Post(RefreshChrome);
        RefreshChrome();
    }

    /// <summary>Set by App when the application ends: then the window really closes.</summary>
    public bool IsExiting { get; set; }

    /// <summary>The library: the installed apps (the start page).</summary>
    public AppsPage Apps => _apps;

    /// <summary>The store: every app.</summary>
    public StorePage Store => _store;

    /// <summary>The page of one app (built when it is first shown).</summary>
    public AppDetailPage Detail => _detail.Value;

    /// <summary>The buttons of the installed apps in the title bar (tests).</summary>
    public IReadOnlyList<Button> QuickLaunchButtons => QuickLaunch.Children.OfType<Button>().ToList();

    public bool IsSidebarCollapsed => Sidebar.IsFolded;

    /// <summary>The version at the foot of the menu (<c>Version 1.0.0 · release</c>, SETUP-07): it opens About.</summary>
    public Button VersionButton => Sidebar.VersionButton;

    public void ToggleSidebar() => Sidebar.IsFolded = !Sidebar.IsFolded;

    /// <summary>Shows the window (tray, second start, Dock) and brings it to the front.</summary>
    public void ShowWindow()
    {
        BringToFront();
        AppService.DetectInstalled(); // an app installed or removed by hand meanwhile
    }

    /// <summary>The library with a filter (Installed – every installed app – or Updates); optionally an app chosen.</summary>
    public void ShowApps(AppFilter? filter = null, string? select = null)
    {
        if (filter is { } chosen)
        {
            _apps.SetFilter(chosen);
        }

        PageHost.Content = _apps;
        Sidebar.Select(NavItemOf(_apps.Filter));
        if (select is not null)
        {
            _apps.Select(select);
        }

        Dispatcher.UIThread.Post(_apps.FocusList, DispatcherPriority.Background);
    }

    /// <summary>The store; optionally an app chosen.</summary>
    public void ShowStore(string? select = null)
    {
        PageHost.Content = _store;
        Sidebar.Select(_navStore);
        if (select is not null)
        {
            _store.Select(select);
        }

        Dispatcher.UIThread.Post(_store.FocusList, DispatcherPriority.Background);
    }

    /// <summary>The page of one app; "Zurück" goes to the page it was opened from (the store or the library).</summary>
    public void ShowDetails(string appId)
    {
        if (PageHost.Content != Detail)
        {
            _detailFromStore = PageHost.Content == _store;
        }

        Detail.BackText = _detailFromStore ? "Store" : _apps.Filter == AppFilter.Updates ? "Updates" : "Bibliothek";
        Detail.Show(appId);
        PageHost.Content = Detail;
    }

    public void ShowSettings(string? tab = null)
    {
        _settings.Value.SelectTab(tab ?? SettingsPage.GeneralTab);
        PageHost.Content = _settings.Value;
        Sidebar.Select(_navSettings);
    }

    public void ShowAbout()
    {
        PageHost.Content = _about.Value;
        Sidebar.Select(null);
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // DESK-06: closing hides; ThixLauncher keeps running in the tray / menu bar.
        if (!IsExiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private MatNavItem NavItemOf(AppFilter filter) => filter == AppFilter.Updates ? _navUpdates : _navLibrary;

    private void OnNavInvoked(MatNavItem item)
    {
        if (item == _navSettings)
        {
            ShowSettings();
            return;
        }

        if (item == _navStore)
        {
            ShowStore();
            return;
        }

        ShowApps(item == _navUpdates ? AppFilter.Updates : AppFilter.Installed);
    }

    /// <summary>From an app's page back to where it was opened, with the app chosen.</summary>
    private void GoBackFromDetails()
    {
        var appId = Detail.AppId;
        if (_detailFromStore)
        {
            ShowStore(appId);
            return;
        }

        ShowApps(null, appId);
    }

    private void PostRefresh()
    {
        if (_refreshPosted)
        {
            return;
        }

        _refreshPosted = true;
        Ui.Post(() =>
        {
            _refreshPosted = false;
            RefreshChrome();
        });
    }

    /// <summary>The installed apps in the title bar, the number of updates in the menu, a waiting update of ThixLauncher at its foot.</summary>
    private void RefreshChrome()
    {
        var all = AppService.All();
        var updates = all.Count(status => status.HasUpdate);
        _navUpdates.Label = updates > 0 ? $"Updates ({updates})" : "Updates";
        Sidebar.UpdateVersion = UpdateService.LastResult is { IsUpdateAvailable: true, Manifest: { } manifest } ? manifest.Version : null;

        var installed = all.Where(status => status.IsInstalled).ToList();
        var current = QuickLaunch.Children.OfType<Button>().Select(button => button.Tag as string).ToList();
        if (!current.SequenceEqual(installed.Select(status => status.App.Id)))
        {
            QuickLaunch.Children.Clear();
            foreach (var status in installed)
            {
                QuickLaunch.Children.Add(QuickLaunchButton(status.App));
            }
        }

        foreach (var button in QuickLaunch.Children.OfType<Button>())
        {
            var status = installed.First(each => each.App.Id == (string)button.Tag!);
            button.IsEnabled = status.CanLaunch;
        }

        QuickLaunchLabel.IsVisible = installed.Count > 0;
    }

    private Button QuickLaunchButton(ThixApp app)
    {
        var button = new Button
        {
            Tag = app.Id,
            Theme = Ui.Theme("MatButton.Ghost"),
            Padding = new Thickness(4),
            Content = new AppLogo { AppId = app.Id, FallbackIcon = app.Icon, Size = 30 },
        };
        ToolTip.SetTip(button, $"{app.Name} starten");
        AutomationProperties.SetName(button, $"{app.Name} starten");
        button.Click += (_, _) =>
        {
            try
            {
                AppService.Launch(app.Id);
            }
            catch (BusinessException exception)
            {
                ShowApps(AppFilter.Installed, app.Id);
                _apps.ShowMessage(exception.Message, InfoSeverity.Error);
            }
        };
        return button;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var command = Ui.CommandKey.Modifier;
        if (e.KeyModifiers == command)
        {
            switch (e.Key)
            {
                case Key.D1:
                    ShowApps(AppFilter.Installed);
                    e.Handled = true;
                    return;
                case Key.D2:
                    ShowStore();
                    e.Handled = true;
                    return;
                case Key.D3:
                    ShowApps(AppFilter.Updates);
                    e.Handled = true;
                    return;
                case Key.F:
                    // The store searches every app, the library the installed ones (after the focus Show… gives the list).
                    if (PageHost.Content == _store || (PageHost.Content == Detail && _detailFromStore))
                    {
                        ShowStore();
                        Dispatcher.UIThread.Post(_store.FocusSearch, DispatcherPriority.Background);
                    }
                    else
                    {
                        ShowApps();
                        Dispatcher.UIThread.Post(_apps.FocusSearch, DispatcherPriority.Background);
                    }

                    e.Handled = true;
                    return;
                case Key.R:
                    ShowApps();
                    _ = _apps.CheckForUpdatesAsync();
                    e.Handled = true;
                    return;
                case Key.OemComma:
                    ShowSettings();
                    e.Handled = true;
                    return;
                case Key.B:
                    ToggleSidebar();
                    e.Handled = true;
                    return;
            }
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F5)
        {
            ShowApps();
            _ = _apps.CheckForUpdatesAsync();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape)
        {
            if (PageHost.Content == _apps && _apps.SearchBox.IsFocused && !string.IsNullOrEmpty(_apps.SearchBox.Text))
            {
                _apps.SearchBox.Text = "";
            }
            else if (PageHost.Content == _store && _store.SearchBox.IsFocused && !string.IsNullOrEmpty(_store.SearchBox.Text))
            {
                _store.SearchBox.Text = "";
            }
            else if (PageHost.Content is AppDetailPage)
            {
                GoBackFromDetails();
            }
            else if (PageHost.Content != _apps)
            {
                ShowApps();
            }
            else
            {
                Hide();
            }

            e.Handled = true;
        }
    }
}
