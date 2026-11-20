using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Pages;

/// <summary>
/// The store: every app of the catalog. One app big at the top (<see cref="StoreService.Featured"/> first – the first one not
/// installed –, ‹ › go through all), every app as a tile with its cover below. A click on a tile (or Enter) opens the app's page
/// (<see cref="DetailsRequested"/>), the button on a tile acts at once. Every card is built once and shows each new state.
/// </summary>
public partial class StorePage : UserControl
{
    private readonly Dictionary<string, AppCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private string? _heroId;
    private bool _refreshPosted;

    public StorePage()
    {
        InitializeComponent();
        AppList.ItemContainerTheme = this.TryFindResource("AppTile", out var tile) && tile is ControlTheme theme
            ? theme
            : throw new InvalidOperationException("The control theme AppTile is missing (Pages/TileStyles.axaml).");
        foreach (var app in AppCatalog.Apps)
        {
            var card = new AppCard(app, AppsView.Tiles);
            card.ActionRequested += async (_, action) => await RunAsync(action, card.Status!);
            _cards[app.Id] = card;
        }

        SearchBox.TextChanged += (_, _) => Refresh();
        AppList.Tapped += OnListTapped;
        AppList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        HeroPictureFrame.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                OpenHeroDetails();
            }
        };

        AppService.Changed += (_, _) => PostRefresh();
        SettingsService.Changed += (_, _) => PostRefresh();
        Refresh();
    }

    /// <summary>The page of an app was asked for (a tile, Enter, "Mehr erfahren", the big picture).</summary>
    public event EventHandler<string>? DetailsRequested;

    /// <summary>The tiles shown, in their order (tests).</summary>
    public IReadOnlyList<AppCard> Cards => AppList.ItemsSource?.OfType<AppCard>().ToList() ?? [];

    /// <summary>The app shown big (null: none, e.g. while searching).</summary>
    public string? HeroAppId => Hero.IsVisible ? _heroId : null;

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>The keyboard goes to the tiles (arrows choose, Enter opens the app's page).</summary>
    public void FocusList()
    {
        if (AppList.SelectedItem is { } selected && AppList.ContainerFromItem(selected) is { } container)
        {
            container.Focus(NavigationMethod.Directional);
            return;
        }

        AppList.Focus();
    }

    /// <summary>Chooses the tile of an app (and scrolls to it); false when the search hides it.</summary>
    public bool Select(string appId)
    {
        if (!_cards.TryGetValue(appId, out var card) || !Cards.Contains(card))
        {
            return false;
        }

        AppList.SelectedItem = card;
        card.BringIntoView(); // the page scrolls, not the list
        return true;
    }

    /// <summary>Shows <paramref name="appId"/> big.</summary>
    public void ShowHero(string appId)
    {
        _heroId = appId;
        RefreshHero();
    }

    /// <summary>The next (or previous) app big, round the catalog.</summary>
    public void MoveHero(int step)
    {
        var all = AppService.All();
        if (all.Count == 0)
        {
            return;
        }

        var index = Math.Max(0, all.ToList().FindIndex(status => status.App.Id == _heroId));
        _heroId = all[((index + step) % all.Count + all.Count) % all.Count].App.Id;
        RefreshHero();
    }

    /// <summary>Asks the business layer for the apps that match the search and shows them.</summary>
    public void Refresh()
    {
        var search = (SearchBox.Text ?? "").Trim();
        var statuses = AppService.Query(new AppQuery(search, AppFilter.All, AppSort.Name));
        foreach (var status in statuses)
        {
            _cards[status.App.Id].Show(status);
        }

        var cards = statuses.Select(status => _cards[status.App.Id]).ToList();
        if (!cards.SequenceEqual(Cards))
        {
            var selected = AppList.SelectedItem as AppCard;
            AppList.ItemsSource = cards;
            AppList.SelectedItem = selected is not null && cards.Contains(selected) ? selected : null;
        }

        var all = AppService.All();
        var notInstalled = all.Count(status => !status.IsInstalled);
        SubtitleText.Text = $"{all.Count} Thix-Apps · {notInstalled} noch nicht installiert · direkt von GitHub, jedes Paket mit SHA-256 geprüft · Kanal {AppService.ChannelName}";
        SectionTitle.Text = search.Length > 0 ? $"Suche nach „{search}“" : "Alle Apps";
        SectionText.Text = search.Length > 0
            ? cards.Count == 1 ? "1 App" : $"{cards.Count} Apps"
            : "Klick auf eine App für Bilder, Änderungen und Details.";
        EmptyState.IsVisible = cards.Count == 0;
        EmptyText.Text = $"Keine App passt zu „{search}“.";

        Hero.IsVisible = search.Length == 0 && all.Count > 0;
        _heroId ??= StoreService.Featured(all)?.App.Id;
        RefreshHero();
    }

    private void RefreshHero()
    {
        if (_heroId is null || AppService.Get(_heroId) is not { } status)
        {
            return;
        }

        var app = status.App;
        var all = AppService.All();
        HeroEyebrow.Text = !status.IsInstalled && status.Latest is not null ? "NEU FÜR DICH" : status.HasUpdate ? "UPDATE VERFÜGBAR" : "AUS DEINER BIBLIOTHEK";
        HeroPicture.Show(app, app.Screenshots.FirstOrDefault());
        HeroLogo.AppId = app.Id;
        HeroLogo.FallbackIcon = app.Icon;
        HeroName.Text = app.Name;
        HeroTagline.Text = app.Tagline;
        HeroDescription.Text = app.Description;
        HeroBadge.Show(status);
        HeroVersion.Text = Ui.ShortVersionLine(status);
        HeroProgress.IsVisible = status.IsBusy;
        HeroProgress.IsIndeterminate = status.Progress?.Fraction is null;
        HeroProgress.Value = status.Progress?.Fraction ?? 0;
        HeroPosition.Text = $"{all.ToList().FindIndex(each => each.App.Id == app.Id) + 1} / {all.Count}";

        var primary = AppActions.Primary(status);
        HeroActionLabel.Icon = primary.Icon;
        HeroActionLabel.Text = primary.Text;
        HeroAction.IsEnabled = primary.Action is not null;
        ToolTip.SetTip(HeroAction, primary.Tip);
        ToolTip.SetTip(HeroPictureFrame, $"{app.Name}: Bilder, Änderungen und Details");
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

    private async Task RunAsync(AppAction action, AppStatus status)
    {
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
    }

    /// <summary>A click on a tile opens the app's page – not a click on the tile's button (that one acts).</summary>
    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Visual;
        if (source?.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (source?.FindAncestorOfType<AppCard>(includeSelf: true) is { } card)
        {
            DetailsRequested?.Invoke(this, card.App.Id);
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && AppList.SelectedItem is AppCard card)
        {
            DetailsRequested?.Invoke(this, card.App.Id);
            e.Handled = true;
        }
    }

    private void OpenHeroDetails()
    {
        if (_heroId is not null)
        {
            DetailsRequested?.Invoke(this, _heroId);
        }
    }

    private async void OnHeroActionClick(object? sender, RoutedEventArgs e)
    {
        if (_heroId is not null && AppService.Get(_heroId) is { } status && AppActions.Primary(status).Action is { } action)
        {
            await RunAsync(action, status);
        }
    }

    private void OnHeroMoreClick(object? sender, RoutedEventArgs e) => OpenHeroDetails();

    private void OnPreviousClick(object? sender, RoutedEventArgs e) => MoveHero(-1);

    private void OnNextClick(object? sender, RoutedEventArgs e) => MoveHero(1);
}
