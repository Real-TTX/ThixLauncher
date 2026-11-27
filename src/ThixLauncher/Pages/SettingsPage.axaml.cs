using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MatCloud.Contract;
using MatDesktop;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using ThixLauncher.Dialogs;

namespace ThixLauncher.Pages;

/// <summary>
/// The settings. Every change goes through the business layer (SettingsService, AutostartService, CloudService, UpdateService), which
/// checks values and policies; an error is shown at its field or above the tabs.
/// </summary>
public partial class SettingsPage : UserControl
{
    public const string GeneralTab = "general";
    public const string AppsTab = "apps";
    public const string CloudTab = "cloud";
    public const string UpdatesTab = "updates";

    private bool _isLoading;
    private UpdateManifest? _availableUpdate;
    private CancellationTokenSource? _browserSignIn;
    private string? _browserAddress;

    public SettingsPage()
    {
        InitializeComponent();

        AutostartSwitch.IsCheckedChanged += (_, _) => Apply(() =>
        {
            if (AutostartSwitch.IsChecked == true)
            {
                AutostartService.Enable(App.ExecutablePath);
            }
            else
            {
                AutostartService.Disable();
            }
        });
        ThemeChoice.SelectionChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { Theme = (ThemeChoice)Math.Max(0, ThemeChoice.SelectedIndex) }));
        ChannelChoice.SelectionChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { AppChannel = (AppChannel)Math.Max(0, ChannelChoice.SelectedIndex) }));
        AutoUpdateAppsSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { UpdateAppsAutomatically = AutoUpdateAppsSwitch.IsChecked == true }));
        NotifySwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { NotifyAboutAppUpdates = NotifySwitch.IsChecked == true }));
        CheckUpdatesSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { CheckForUpdates = CheckUpdatesSwitch.IsChecked == true }));
        AutoInstallSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { InstallUpdatesAutomatically = AutoInstallSwitch.IsChecked == true }));

        ConnectAppsSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { ConnectInstalledApps = ConnectAppsSwitch.IsChecked == true }));

        SettingsService.Changed += (_, _) => Ui.Post(Load);
        CloudService.StatusChanged += (_, _) => Ui.Post(LoadCloud);
        UpdateCoordinator.Checked += (_, result) => ShowUpdateResult(result);
        Load();
        if (UpdateService.LastResult is { } last)
        {
            ShowUpdateResult(last);
        }
    }

    public void SelectTab(string tab) => Tabs.SelectedItem = tab switch
    {
        AppsTab => AppsTabItem,
        CloudTab => CloudTabItem,
        UpdatesTab => UpdatesTabItem,
        _ => GeneralTabItem,
    };

    /// <summary>Shows the stored settings (again after every change, also from the tray).</summary>
    private void Load()
    {
        _isLoading = true;
        try
        {
            var settings = SettingsService.Get();
            var locked = AccessPolicy.IsConfigurationLocked;
            PolicyNote.IsVisible = AccessPolicy.IsAnySet;

            AutostartSwitch.IsChecked = AutostartService.IsEnabled();
            AutostartSwitch.IsEnabled = AutostartService.IsSupported && !locked;
            ThemeChoice.SelectedIndex = (int)settings.Theme;
            ChannelChoice.SelectedIndex = (int)settings.AppChannel;
            AutoUpdateAppsSwitch.IsChecked = settings.UpdateAppsAutomatically;
            NotifySwitch.IsChecked = settings.NotifyAboutAppUpdates;
            ThemeChoice.IsEnabled = ChannelChoice.IsEnabled = NotifySwitch.IsEnabled = !locked;
            AutoUpdateAppsSwitch.IsEnabled = !locked && !AccessPolicy.AreAppChangesDisabled && AppService.IsSupported;

            CheckUpdatesSwitch.IsChecked = settings.CheckForUpdates;
            AutoInstallSwitch.IsChecked = settings.InstallUpdatesAutomatically;
            InstalledVersionText.Text = MatVersion.Value(AppInfo.Version, AppInfo.Channel);
            ChannelText.Text = AppInfo.Channel switch
            {
                "release" => "Release (Branch main)",
                "nightly" => "Nightly (Branch dev)",
                _ => "Lokaler Build – keine Updates",
            };
            LastCheckText.Text = UpdateService.LastCheck is { } last ? Ui.FormatDateTime(last) : "–";
            CheckUpdateButton.IsEnabled = UpdateService.CanUpdate;
            CheckUpdatesSwitch.IsEnabled = !locked;
            AutoInstallSwitch.IsEnabled = UpdateService.CanUpdate && !locked;

            DataFolderRow.Description = AppPaths.DataDirectory;
            LogFolderRow.Description = AppPaths.LogDirectory;

            ConnectAppsSwitch.IsChecked = settings.ConnectInstalledApps;
            ConnectAppsSwitch.IsEnabled = !locked;
            LoadCloud();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Apply(Action change)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            FormErrors.ShowError(this, null, "");
            PageMessage.Hide();
            change();
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                PageMessage.Show(exception.Message, InfoSeverity.Error);
            }

            Load();
        }
    }

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        try
        {
            ShowUpdateResult(await UpdateCoordinator.CheckAsync());
        }
        catch (BusinessException exception)
        {
            UpdateMessage.Show(exception.Message, InfoSeverity.Error);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = UpdateService.CanUpdate;
            LastCheckText.Text = UpdateService.LastCheck is { } last ? Ui.FormatDateTime(last) : "–";
        }
    }

    private void ShowUpdateResult(UpdateCheckResult result)
    {
        _availableUpdate = result.Manifest;
        InstallUpdateButton.IsVisible = result.IsUpdateAvailable;
        UpdateMessage.Show(result.Message, result.IsUpdateAvailable ? InfoSeverity.Success : InfoSeverity.Info);
    }

    private async void OnInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null)
        {
            return;
        }

        InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;
        try
        {
            await UpdateCoordinator.InstallAsync(_availableUpdate, new Progress<double>(value => UpdateProgress.Value = value), showWindow: true);
        }
        catch (Exception exception) when (exception is BusinessException or HttpRequestException or IOException)
        {
            UpdateMessage.Show($"Das Update ist fehlgeschlagen: {exception.Message}", InfoSeverity.Error);
            InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = true;
            UpdateProgress.IsVisible = false;
        }
    }

    private void OnReleasePageClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(UpdateService.ReleasePageUrl(AppInfo.IsLocalBuild ? "release" : AppInfo.Channel));

    private void OnOpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Ui.OpenExternal(AppPaths.DataDirectory);
    }

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Ui.OpenExternal(AppPaths.LogDirectory);
    }

    /// <summary>The cloud tab: the sign-in while not connected, the account and its actions while connected.</summary>
    private void LoadCloud()
    {
        var settings = SettingsService.Get();
        var cloud = settings.Cloud;
        var status = CloudService.Status;
        CloudLogin.IsVisible = !cloud.IsConnected;
        CloudConnected.IsVisible = cloud.IsConnected;
        CloudLogin.IsEnabled = !AccessPolicy.IsCloudDisabled;
        if (AccessPolicy.IsCloudDisabled)
        {
            CloudMessage.Show("Von deiner Organisation festgelegt: die MatCloud ist ausgeschaltet (Richtlinie DisableCloud).", InfoSeverity.Warning);
        }
        else if (status.State == CloudState.SignedOut)
        {
            CloudMessage.Show(status.Message, InfoSeverity.Warning);
        }

        if (!cloud.IsConnected)
        {
            if (string.IsNullOrEmpty(ServerUrlBox.Text))
            {
                ServerUrlBox.Text = cloud.ServerUrl;
            }

            if (string.IsNullOrEmpty(UserNameBox.Text))
            {
                UserNameBox.Text = cloud.UserName;
            }

            if (string.IsNullOrEmpty(DeviceNameBox.Text))
            {
                DeviceNameBox.Text = cloud.DeviceName.Length > 0 ? cloud.DeviceName : AppInfo.MachineName;
            }

            return;
        }

        CloudServerText.Text = cloud.ServerUrl;
        CloudAccountText.Text = cloud.AccountText;
        CloudDeviceText.Text = cloud.DeviceName;
        CloudStateText.Text = status.State == CloudState.Offline
            ? $"{status.Message} Apps werden trotzdem installiert und melden sich später selbst an."
            : status.Message;
    }

    /// <summary>The first way: MatCloud shows a code, the browser opens, the person confirms it there; ThixLauncher waits (Abbrechen ends it).</summary>
    private async void OnBrowserConnectClick(object? sender, RoutedEventArgs e)
    {
        if (_browserSignIn is not null)
        {
            return;
        }

        CloudMessage.Hide();
        FormErrors.ShowError(this, null, "");
        _browserSignIn = new CancellationTokenSource();
        ServerUrlBox.IsEnabled = DeviceNameBox.IsEnabled = BrowserConnectButton.IsEnabled = ShowPasswordSignInButton.IsEnabled = false;
        try
        {
            if (await CloudService.ConnectInBrowserAsync(ServerUrlBox.Text ?? "", DeviceNameBox.Text ?? "", start => Ui.Post(() => ShowBrowserCode(start)),
                    Ui.OpenExternal, _browserSignIn.Token))
            {
                CloudMessage.Show(ConnectedText(), InfoSeverity.Success);
            }
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                CloudMessage.Show(exception.Message, InfoSeverity.Error);
            }
        }
        finally
        {
            _browserSignIn.Dispose();
            _browserSignIn = null;
            ServerUrlBox.IsEnabled = DeviceNameBox.IsEnabled = BrowserConnectButton.IsEnabled = ShowPasswordSignInButton.IsEnabled = true;
            ShowSignInWay(password: false);
            LoadCloud();
        }
    }

    /// <summary>The code of a started sign-in in the browser, big, with the hint to confirm it there.</summary>
    internal void ShowBrowserCode(DeviceLinkStart start)
    {
        _browserAddress = start.VerificationUrl;
        BrowserCodeText.Text = start.Code;
        BrowserSignIn.IsVisible = PasswordSignIn.IsVisible = false;
        BrowserCode.IsVisible = true;
        FocusAfterLayout(CancelLinkButton);
    }

    /// <summary>The way to sign in: in the browser (first) or with user name and password (second).</summary>
    internal void ShowSignInWay(bool password)
    {
        BrowserCode.IsVisible = false;
        BrowserSignIn.IsVisible = !password;
        PasswordSignIn.IsVisible = password;
    }

    private void OnShowPasswordSignInClick(object? sender, RoutedEventArgs e)
    {
        ShowSignInWay(password: true);
        FocusAfterLayout(string.IsNullOrEmpty(UserNameBox.Text) ? UserNameBox : PasswordBox);
    }

    private void OnShowBrowserSignInClick(object? sender, RoutedEventArgs e)
    {
        ShowSignInWay(password: false);
        FocusAfterLayout(BrowserConnectButton);
    }

    /// <summary>A control that has just become visible takes the focus once it is laid out (before that it cannot).</summary>
    private static void FocusAfterLayout(Control control) =>
        Dispatcher.UIThread.Post(() => control.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    private void OnOpenLinkClick(object? sender, RoutedEventArgs e)
    {
        if (_browserAddress is not null)
        {
            Ui.OpenExternal(_browserAddress);
        }
    }

    private void OnCancelLinkClick(object? sender, RoutedEventArgs e) => _browserSignIn?.Cancel();

    /// <summary>The second way: user name and password of a local account of MatCloud.</summary>
    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        CloudMessage.Hide();
        try
        {
            FormErrors.ShowError(this, null, "");
            await CloudService.ConnectAsync(ServerUrlBox.Text ?? "", UserNameBox.Text ?? "", PasswordBox.Text ?? "", DeviceNameBox.Text ?? "");
            PasswordBox.Text = "";
            CloudMessage.Show(ConnectedText(), InfoSeverity.Success);
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                CloudMessage.Show(exception.Message, InfoSeverity.Error);
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            LoadCloud();
        }
    }

    private static string ConnectedText() => "Verbunden. Apps, die du ab jetzt mit ThixLauncher installierst, melden sich mit diesem Konto an.";

    private void OnOpenCloudClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(SettingsService.Get().Cloud.ServerUrl);

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDialog.AskAsync(Ui.WindowOf(this), "Von der MatCloud abmelden?",
                "ThixLauncher wird aus deinem Konto der MatCloud entfernt; neue Apps melden sich dann selbst an. Apps, die schon verbunden sind, bleiben verbunden.",
                "Abmelden", isDestructive: true))
        {
            return;
        }

        await CloudService.DisconnectAsync();
        CloudMessage.Show("Abgemeldet.", InfoSeverity.Info);
        LoadCloud();
    }
}
