using Avalonia.Controls;
using Avalonia.Interactivity;
using MatDesktop;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Pages;

/// <summary>
/// The settings. Every change goes through the business layer (SettingsService, AutostartService, UpdateService), which
/// checks values and policies; an error is shown at its field or above the tabs.
/// </summary>
public partial class SettingsPage : UserControl
{
    public const string GeneralTab = "general";
    public const string AppsTab = "apps";
    public const string UpdatesTab = "updates";

    private bool _isLoading;
    private UpdateManifest? _availableUpdate;

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

        SettingsService.Changed += (_, _) => Ui.Post(Load);
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
            await UpdateCoordinator.InstallAsync(_availableUpdate, new Progress<double>(value => UpdateProgress.Value = value));
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
}
