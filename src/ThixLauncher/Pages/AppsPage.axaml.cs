using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Pages;

/// <summary>
/// The library: the installed apps, as app tiles (the logo big) or as a list (<see cref="View"/>, remembered in the
/// settings), the last tile "Apps hinzufügen" opens the store (<see cref="StoreRequested"/>). The toolbar beside the title (search,
/// filter "Alle installierten" / "Mit Update", sort) asks <see cref="AppService.Query"/>; every card is built once and shows each
/// new state (a running installation updates its card many times a second without rebuilding the list). Enter or a double click
/// opens the details; the actions below act on the chosen app.
/// </summary>
public partial class AppsPage : UserControl
{
    /// <summary>Below this width of the page the toolbar goes under the title.</summary>
    private const double WideHeaderWidth = 900;

    private static readonly FuncTemplate<Panel?> TilePanel = new(() => new WrapPanel());
    private static readonly FuncTemplate<Panel?> ListPanel = new(() => new StackPanel());

    private readonly ControlTheme _rowTheme;
    private readonly ControlTheme _tileTheme;
    private readonly Dictionary<string, AppCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly AddAppsCard _addCard;
    private bool _refreshPosted;
    private bool _isLoading;

    public AppsPage()
    {
        InitializeComponent();
        _rowTheme = AppList.ItemContainerTheme!;
        _tileTheme = this.TryFindResource("AppTile", out var tile) && tile is ControlTheme theme
            ? theme
            : throw new InvalidOperationException("The control theme AppTile is missing (Pages/TileStyles.axaml).");
        View = SettingsService.Get().AppsView;
        _addCard = new AddAppsCard(View);
        foreach (var app in AppCatalog.Apps)
        {
            var card = new AppCard(app, View);
            card.ActionRequested += async (_, action) => await RunAsync(action, card.Status!);
            _cards[app.Id] = card;
        }

        ShowView(View, remember: false);
        ToolTip.SetTip(RefreshButton, $"Nach Updates suchen ({Ui.CommandKey.Name}+R)");
        SizeChanged += (_, e) => ArrangeHeader(e.NewSize.Width);

        _isLoading = true;
        FilterChoice.SelectedIndex = 0;
        SortChoice.SelectedIndex = 0;
        _isLoading = false;

        SearchBox.TextChanged += (_, _) => Refresh();
        FilterChoice.SelectionChanged += (_, _) => OnToolbarChanged();
        SortChoice.SelectionChanged += (_, _) => OnToolbarChanged();
        AppList.SelectionChanged += (_, _) => RefreshActions();
        AppList.DoubleTapped += (_, _) => OpenDetails();
        AppList.Tapped += (_, e) =>
        {
            if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<AddAppsCard>(includeSelf: true) is not null)
            {
                StoreRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        // The list takes Enter for itself (choosing): the details listen to handled keys too.
        AppList.AddHandler(KeyDownEvent, OnListKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        AppService.Changed += (_, _) => PostRefresh();
        SettingsService.Changed += (_, _) => PostRefresh();
        Refresh();
        if (AppList.ItemCount > 0)
        {
            AppList.SelectedIndex = 0;
        }
    }

    /// <summary>"Details" was asked for an app (Enter, double click, the button).</summary>
    public event EventHandler<string>? DetailsRequested;

    /// <summary>"Apps hinzufügen" was chosen: the store.</summary>
    public event EventHandler? StoreRequested;

    /// <summary>The filter changed (the menu marks its item).</summary>
    public event EventHandler<AppFilter>? FilterChanged;

    /// <summary>Installed (every installed app) or Updates (those with an update).</summary>
    public AppFilter Filter => FilterChoice.SelectedIndex == 1 ? AppFilter.Updates : AppFilter.Installed;

    /// <summary>The chosen app (null: none).</summary>
    public AppStatus? Selected => (AppList.SelectedItem as AppCard)?.Status;

    /// <summary>The cards shown, in their order (tests).</summary>
    public IReadOnlyList<AppCard> Cards => AppList.ItemsSource?.OfType<AppCard>().ToList() ?? [];

    /// <summary>The last tile "Apps hinzufügen" (shown with every installed app, not with the filter "Mit Update" or a search).</summary>
    public AddAppsCard AddCard => _addCard;

    /// <summary>True when the last tile "Apps hinzufügen" is shown.</summary>
    public bool ShowsAddCard => AppList.ItemsSource?.OfType<AddAppsCard>().Any() == true;

    /// <summary>Tiles or list.</summary>
    public AppsView View { get; private set; }

    public void SetFilter(AppFilter filter) => FilterChoice.SelectedIndex = filter == AppFilter.Updates ? 1 : 0;

    /// <summary>Shows the apps as tiles or as a list (the chosen app stays chosen) and remembers it.</summary>
    public void ShowView(AppsView view, bool remember = true)
    {
        View = view;
        TilesButton.Classes.Set("current", view == AppsView.Tiles);
        ListButton.Classes.Set("current", view == AppsView.List);
        foreach (var card in _cards.Values)
        {
            card.ShowView(view);
        }

        _addCard.ShowView(view);

        // New containers for the new look: the list lets go of its items and takes them again.
        var selected = AppList.SelectedItem;
        var items = AppList.ItemsSource;
        AppList.ItemsSource = null;
        AppList.ItemContainerTheme = view == AppsView.Tiles ? _tileTheme : _rowTheme;
        AppList.ItemsPanel = view == AppsView.Tiles ? TilePanel : ListPanel;
        AppList.ItemsSource = items;
        AppList.SelectedItem = selected;
        if (remember)
        {
            SettingsService.Remember(current => current with { AppsView = view });
        }
    }

    /// <summary>The keyboard goes to the chosen app of the list (arrows choose, Enter opens the details).</summary>
    public void FocusList()
    {
        if (AppList.SelectedItem is { } selected && AppList.ContainerFromItem(selected) is { } container)
        {
            container.Focus(NavigationMethod.Directional);
            return;
        }

        AppList.Focus();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Chooses the app (and scrolls to it); false when the filter hides it.</summary>
    public bool Select(string appId)
    {
        if (!_cards.TryGetValue(appId, out var row) || !Cards.Contains(row))
        {
            return false;
        }

        AppList.SelectedItem = row;
        AppList.ScrollIntoView(row);
        return true;
    }

    /// <summary>Shows a message above the toolbar (the result of an action from elsewhere, e.g. the tray).</summary>
    public void ShowMessage(string message, InfoSeverity severity) => PageMessage.Show(message, severity);

    /// <summary>"Nach Updates suchen": reads every feed again.</summary>
    public async Task CheckForUpdatesAsync()
    {
        RefreshButton.IsEnabled = false;
        PageMessage.Hide();
        try
        {
            await AppCoordinator.RefreshAsync(automatic: false);
            var updates = AppService.Count(AppFilter.Updates);
            var errors = AppService.All().Where(status => status.FeedError is not null).Select(status => $"{status.App.Name}: {status.FeedError}").ToList();
            if (errors.Count > 0)
            {
                PageMessage.Show(string.Join(Environment.NewLine, errors), InfoSeverity.Warning);
            }
            else
            {
                PageMessage.Show(updates switch
                {
                    0 => "Alle installierten Apps sind aktuell.",
                    1 => "Für eine App gibt es ein Update.",
                    _ => $"Für {updates} Apps gibt es Updates.",
                }, updates > 0 ? InfoSeverity.Success : InfoSeverity.Info);
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    /// <summary>Asks the business layer for the list of the toolbar and shows it; the chosen app stays chosen.</summary>
    public void Refresh()
    {
        var selected = AppList.SelectedItem as Control;
        var search = (SearchBox.Text ?? "").Trim();
        var statuses = AppService.Query(new AppQuery(search, Filter, (AppSort)Math.Max(0, SortChoice.SelectedIndex)));
        var rows = statuses.Select(status => (Control)_cards[status.App.Id]).ToList();
        if (Filter == AppFilter.Installed && search.Length == 0)
        {
            rows.Add(_addCard);
        }

        foreach (var status in statuses)
        {
            _cards[status.App.Id].Show(status);
        }

        if (!rows.SequenceEqual(AppList.ItemsSource?.OfType<Control>() ?? []))
        {
            AppList.ItemsSource = rows;
            AppList.SelectedItem = selected is not null && rows.Contains(selected) ? selected : rows.FirstOrDefault();
        }

        EmptyState.IsVisible = rows.Count == 0;
        EmptyText.Text = search.Length > 0
            ? $"Keine installierte App passt zu „{search}“ – im Store gibt es alle Apps."
            : Filter == AppFilter.Updates ? "Alles aktuell – keine Updates." : "Noch keine App installiert.";
        RefreshSubtitle();
        RefreshActions();
    }

    private void OnToolbarChanged()
    {
        if (_isLoading)
        {
            return;
        }

        Refresh();
        FilterChanged?.Invoke(this, Filter);
    }

    /// <summary>A burst of changes (progress of a download) is one refresh.</summary>
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

    private void RefreshSubtitle()
    {
        var all = AppService.All();
        var installed = all.Count(status => status.IsInstalled);
        var updates = all.Count(status => status.HasUpdate);
        var parts = new List<string> { installed == 1 ? "1 App installiert" : $"{installed} Apps installiert" };
        if (updates > 0)
        {
            parts.Add(updates == 1 ? "1 Update" : $"{updates} Updates");
        }

        parts.Add($"Kanal {AppService.ChannelName}");
        parts.Add(AppService.IsRefreshing ? "wird geprüft …"
            : AppService.LastRefresh is { } last ? $"geprüft {Ui.FormatDateTime(last)}" : "noch nicht geprüft");
        SubtitleText.Text = string.Join(" · ", parts);
        TitleText.Text = Filter == AppFilter.Updates ? "Updates" : "Bibliothek";
    }

    /// <summary>The actions below the list follow the chosen app.</summary>
    private void RefreshActions()
    {
        var status = Selected;
        Actions.IsVisible = status is not null;
        if (status is null)
        {
            return;
        }

        var primary = AppActions.Primary(status);
        PrimaryLabel.Icon = primary.Icon;
        PrimaryLabel.Text = primary.Text;
        PrimaryButton.IsEnabled = primary.Action is not null;
        ToolTip.SetTip(PrimaryButton, primary.Tip);

        var secondary = AppActions.Secondary(status);
        SecondaryButton.IsVisible = secondary is not null;
        if (secondary is not null)
        {
            SecondaryLabel.Icon = secondary.Icon;
            SecondaryLabel.Text = secondary.Text;
        }

        UninstallButton.IsVisible = status.IsInstalled;
        UninstallButton.IsEnabled = AppActions.CanUninstall(status);
        ToolTip.SetTip(UninstallButton, AccessPolicy.AreAppChangesDisabled ? "Von deiner Organisation festgelegt" : $"{status.App.Name} entfernen – deine Daten bleiben");
    }

    private async Task RunAsync(AppAction action, AppStatus status)
    {
        PageMessage.Hide();
        Select(status.App.Id);
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

    private void OpenDetails()
    {
        if (Selected is { } status)
        {
            DetailsRequested?.Invoke(this, status.App.Id);
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (AppList.SelectedItem is AddAppsCard)
            {
                StoreRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            OpenDetails();
            e.Handled = true;
        }
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } status && AppActions.Primary(status).Action is { } action)
        {
            await RunAsync(action, status);
        }
    }

    private async void OnSecondaryClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } status && AppActions.Secondary(status)?.Action is { } action)
        {
            await RunAsync(action, status);
        }
    }

    private void OnDetailsClick(object? sender, RoutedEventArgs e) => OpenDetails();

    private void OnTilesClick(object? sender, RoutedEventArgs e) => ShowView(AppsView.Tiles);

    private void OnListClick(object? sender, RoutedEventArgs e) => ShowView(AppsView.List);

    /// <summary>The toolbar beside the title while the page is wide, under the title (left) when it is narrow.</summary>
    private void ArrangeHeader(double width)
    {
        var isWide = width >= WideHeaderWidth;
        Grid.SetRow(Toolbar, isWide ? 0 : 2);
        Grid.SetColumn(Toolbar, isWide ? 1 : 0);
        Grid.SetColumnSpan(Toolbar, isWide ? 1 : 2);
        Toolbar.HorizontalAlignment = isWide ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
        Toolbar.Margin = isWide ? default : new Avalonia.Thickness(0, 14, 0, 0);
    }

    private async void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } status)
        {
            await RunAsync(AppAction.Uninstall, status);
        }
    }
}
