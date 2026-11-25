using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Pages;

/// <summary>
/// The page of one app, like a store: what it is, its pictures from GitHub (<see cref="StoreService.PictureAsync"/>: one big, the
/// others to choose), its newest changes on GitHub (<see cref="StoreService.ChangesAsync"/>), what is installed and what the channel
/// offers, where it lives and how its package is checked, with every action. It follows the state of the app while it is shown
/// (an installation from the store or the tray shows here too).
/// </summary>
public partial class AppDetailPage : UserControl
{
    private bool _refreshPosted;
    private int _changesLoad;
    private string? _changesOf;

    public AppDetailPage()
    {
        InitializeComponent();
        BigPicture.Stretch = Stretch.Uniform;
        AppService.Changed += (_, _) => PostRefresh();
        SettingsService.Changed += (_, _) => PostRefresh();
    }

    /// <summary>"Zurück" (Esc): back to where the page was opened (the store or the library).</summary>
    public event EventHandler? BackRequested;

    /// <summary>The app shown (null: none yet).</summary>
    public string? AppId { get; private set; }

    public AppStatus? Status => AppId is null ? null : AppService.Get(AppId);

    /// <summary>The words of the link back ("Store", "Bibliothek").</summary>
    public string BackText
    {
        get => BackLinkLabel.Text;
        set => BackLinkLabel.Text = value;
    }

    /// <summary>The small pictures under the big one (tests).</summary>
    public IReadOnlyList<Button> ThumbnailButtons => Thumbnails.Children.OfType<Button>().ToList();

    /// <summary>The changes shown, their first lines (tests).</summary>
    public IReadOnlyList<string> ChangeTitles { get; private set; } = [];

    /// <summary>Shows the app with the overview in front and its first picture big; reads its changes from GitHub.</summary>
    public void Show(string appId)
    {
        AppId = appId;
        PageMessage.Hide();
        Tabs.SelectedItem = OverviewTab;
        Scroller.Offset = default;
        ShowGallery(AppService.Get(appId).App);
        Refresh();
        _ = LoadChangesAsync(AppService.Get(appId).App);
    }

    /// <summary>Shows the picture at <paramref name="index"/> big and marks its small one.</summary>
    public void ShowPicture(int index)
    {
        if (Status is not { } status || index < 0 || index >= status.App.Screenshots.Count)
        {
            return;
        }

        var picture = status.App.Screenshots[index];
        BigPicture.Show(status.App, picture);
        CaptionText.Text = $"{picture.Caption}  ·  Bild {index + 1} von {status.App.Screenshots.Count}";
        foreach (var (button, position) in ThumbnailButtons.Select((button, position) => (button, position)))
        {
            button.Classes.Set("current", position == index);
        }
    }

    public void SelectChangesTab() => Tabs.SelectedItem = ChangesTab;

    /// <summary>The big picture and a small button per picture; no pictures – no gallery.</summary>
    private void ShowGallery(ThixApp app)
    {
        Thumbnails.Children.Clear();
        Gallery.IsVisible = app.Screenshots.Count > 0;
        for (var index = 0; index < app.Screenshots.Count; index++)
        {
            var picture = app.Screenshots[index];
            var thumbnail = new AppPicture { Width = 156, Height = 96, DecodeWidth = 320, LogoSize = 32 };
            thumbnail.Show(app, picture);
            var button = new Button { Content = thumbnail, Tag = index };
            button.Classes.Add("thumbnail");
            ToolTip.SetTip(button, picture.Caption);
            Avalonia.Automation.AutomationProperties.SetName(button, $"Bild {index + 1}: {picture.Caption}");
            button.Click += (_, _) => ShowPicture((int)button.Tag!);
            Thumbnails.Children.Add(button);
        }

        if (app.Screenshots.Count > 0)
        {
            ShowPicture(0);
        }
        else
        {
            BigPicture.Show(app, null);
        }
    }

    /// <summary>The newest commits of the app's repository (the branch of the channel), in the tab "Änderungen".</summary>
    private async Task LoadChangesAsync(ThixApp app)
    {
        var load = ++_changesLoad;
        if (_changesOf != app.Id)
        {
            ChangeTitles = [];
            ChangesList.Children.Clear();
        }

        _changesOf = app.Id;
        ChangesIntro.Text = $"Die neuesten Änderungen im Projekt {app.Repository} auf GitHub (Branch {StoreService.ChangesBranch}, Kanal {AppService.ChannelName}) – werden gelesen …";
        ChangesCard.IsVisible = ChangesList.Children.Count > 0;
        try
        {
            var changes = await StoreService.ChangesAsync(app);
            if (load != _changesLoad)
            {
                return;
            }

            ChangesIntro.Text = changes.Count == 0
                ? $"Im Projekt {app.Repository} gibt es noch keine Änderungen auf GitHub."
                : $"Die neuesten Änderungen im Projekt {app.Repository} auf GitHub (Branch {StoreService.ChangesBranch}, Kanal {AppService.ChannelName}).";
            ChangeTitles = changes.Select(change => change.Title).ToList();
            ChangesList.Children.Clear();
            foreach (var change in changes)
            {
                ChangesList.Children.Add(ChangeRow(change));
            }

            ChangesCard.IsVisible = changes.Count > 0;
        }
        catch (BusinessException exception)
        {
            if (load == _changesLoad)
            {
                ChangesIntro.Text = exception.Message;
            }
        }
    }

    /// <summary>One change: the day, the first line of the commit, a click opens it on GitHub.</summary>
    private static Button ChangeRow(AppChange change)
    {
        var day = new TextBlock { Text = Ui.FormatDate(change.Date), Width = 96, VerticalAlignment = VerticalAlignment.Top }
            .Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var title = new TextBlock { Text = change.Title, TextWrapping = TextWrapping.Wrap };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { day, title } };
        Grid.SetColumn(title, 1);
        var button = new Button
        {
            Content = row,
            Theme = Ui.Theme("MatButton.Ghost"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6),
        };
        ToolTip.SetTip(button, "Auf GitHub öffnen");
        button.Click += (_, _) => Ui.OpenExternal(change.Url);
        return button;
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
        Features.Show(app);
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
        FeaturesLabel.IsVisible = FeaturesText.IsVisible = app.Features.Count > 0;
        FeaturesText.Text = string.Join("\n", app.Features.Select(feature => $"{AppFeatures.Name(feature)}: {AppFeatures.Description(feature)}"));

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

    private void OnAllChangesClick(object? sender, RoutedEventArgs e)
    {
        if (Status is { } status)
        {
            Ui.OpenExternal(StoreService.ChangesPage(status.App));
        }
    }
}
