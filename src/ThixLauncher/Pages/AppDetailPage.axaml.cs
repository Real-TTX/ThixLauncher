using Avalonia.Controls;
using Avalonia.Interactivity;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Pages;

/// <summary>
/// The page of one app: what it is, what is installed and what the channel offers, where it lives and how its package is
/// checked, with every action. It follows the state of the app while it is shown (an installation from the list or the
/// tray shows here too).
/// </summary>
public partial class AppDetailPage : UserControl
{
    private bool _refreshPosted;

    public AppDetailPage()
    {
        InitializeComponent();
        AppService.Changed += (_, _) => PostRefresh();
        SettingsService.Changed += (_, _) => PostRefresh();
    }

    /// <summary>"Zurück" (Esc): back to the list.</summary>
    public event EventHandler? BackRequested;

    /// <summary>The app shown (null: none yet).</summary>
    public string? AppId { get; private set; }

    public AppStatus? Status => AppId is null ? null : AppService.Get(AppId);

    /// <summary>Shows the app with the overview in front.</summary>
    public void Show(string appId)
    {
        AppId = appId;
        PageMessage.Hide();
        Tabs.SelectedItem = OverviewTab;
        Refresh();
    }

    public void SelectDetailsTab() => Tabs.SelectedItem = DetailsTab;

    public void Refresh()
    {
        if (Status is not { } status)
        {
            return;
        }

        var app = status.App;
        Logo.AppId = app.Id;
        Logo.FallbackIcon = app.Icon;
        NameText.Text = app.Name;
        TaglineText.Text = app.Tagline;
        DescriptionText.Text = app.Description;
        Badge.Show(status);
        VersionText.Text = Ui.VersionLine(status);
        Progress.IsVisible = status.IsBusy;
        Progress.IsIndeterminate = status.Progress?.Fraction is null;
        Progress.Value = status.Progress?.Fraction ?? 0;

        InstalledText.Text = status.Installed is { } installed ? installed.VersionText ?? "ja (Version unbekannt)" : "Nein";
        AvailableText.Text = status.Latest?.Version.Text ?? status.FeedError ?? "Für dieses System gibt es noch keinen Build.";
        ChannelText.Text = $"{AppService.ChannelName} (für alle Apps, Einstellungen › Apps)";
        PublishedText.Text = status.Latest?.PublishedAt is { } published ? Ui.FormatDate(published) : "–";
        SetupText.Text = app.InstallsQuietly
            ? "Mit einem Klick, ohne Rückfrage"
            : "Das Setup der App öffnet sich und fragt nach Ordner und Optionen";

        LocationText.Text = status.Installed?.Location is { Length: > 0 } location ? location : "–";
        PackageText.Text = status.Latest is { } latest
            ? latest.Size is { } size ? $"{latest.PackageFile} ({Ui.FormatBytes(size)})" : latest.PackageFile
            : "–";
        ChecksumText.Text = status.Latest?.Sha256?.ToLowerInvariant() ?? "–";
        SourceText.Text = status.Latest?.ReleasePage ?? app.ProjectUrl;
        FeedErrorLabel.IsVisible = FeedErrorText.IsVisible = status.FeedError is not null;
        FeedErrorText.Text = status.FeedError;

        var primary = AppActions.Primary(status);
        PrimaryLabel.Icon = primary.Icon;
        PrimaryLabel.Text = primary.Text;
        PrimaryButton.IsEnabled = primary.Action is not null;
        ToolTip.SetTip(PrimaryButton, primary.Tip);
        var secondary = AppActions.Secondary(status);
        SecondaryButton.IsVisible = secondary is not null;
        UninstallButton.IsVisible = status.IsInstalled;
        UninstallButton.IsEnabled = AppActions.CanUninstall(status);
        ToolTip.SetTip(UninstallButton, AccessPolicy.AreAppChangesDisabled ? "Von deiner Organisation festgelegt" : $"{app.Name} entfernen – deine Daten bleiben");
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
            Refresh();
        });
    }

    private async Task RunAsync(AppAction action)
    {
        if (Status is not { } status)
        {
            return;
        }

        PageMessage.Hide();
        try
        {
            if (await AppActions.RunAsync(action, status, Ui.WindowOf(this)) is { } message)
            {
                PageMessage.Show(message, InfoSeverity.Success);
            }
        }
        catch (BusinessException exception)
        {
            PageMessage.Show(exception.Message, InfoSeverity.Error);
        }

        Refresh();
    }

    private async void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (Status is { } status && AppActions.Primary(status).Action is { } action)
        {
            await RunAsync(action);
        }
    }

    private async void OnSecondaryClick(object? sender, RoutedEventArgs e) => await RunAsync(AppAction.Open);

    private async void OnUninstallClick(object? sender, RoutedEventArgs e) => await RunAsync(AppAction.Uninstall);

    private void OnProjectClick(object? sender, RoutedEventArgs e)
    {
        if (Status is { } status)
        {
            Ui.OpenExternal(status.App.ProjectUrl);
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
