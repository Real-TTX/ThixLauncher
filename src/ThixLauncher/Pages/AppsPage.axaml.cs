using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Pages;

/// <summary>
/// The list of the apps. The toolbar (search, filter, sort) asks <see cref="AppService.Query"/>; every row is built once
/// and shows each new state (a running installation updates its row many times a second without rebuilding the list).
/// Enter or a double click opens the details; the actions below act on the chosen app.
/// </summary>
public partial class AppsPage : UserControl
{
    private readonly Dictionary<string, AppRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshPosted;
    private bool _isLoading;

    public AppsPage()
    {
        InitializeComponent();
        foreach (var app in AppCatalog.Apps)
        {
            var row = new AppRow(app);
            row.ActionRequested += async (_, action) => await RunAsync(action, row.Status!);
            _rows[app.Id] = row;
        }

        _isLoading = true;
        FilterChoice.SelectedIndex = 0;
        SortChoice.SelectedIndex = 0;
        _isLoading = false;

        SearchBox.TextChanged += (_, _) => Refresh();
        FilterChoice.SelectionChanged += (_, _) => OnToolbarChanged();
        SortChoice.SelectionChanged += (_, _) => OnToolbarChanged();
        AppList.SelectionChanged += (_, _) => RefreshActions();
        AppList.DoubleTapped += (_, _) => OpenDetails();
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

    /// <summary>The filter changed (the menu marks its item).</summary>
    public event EventHandler<AppFilter>? FilterChanged;

    public AppFilter Filter => (AppFilter)Math.Max(0, FilterChoice.SelectedIndex);

    /// <summary>The chosen app (null: none).</summary>
    public AppStatus? Selected => (AppList.SelectedItem as AppRow)?.Status;

    /// <summary>The rows shown, in their order (tests).</summary>
    public IReadOnlyList<AppRow> Rows => AppList.ItemsSource?.OfType<AppRow>().ToList() ?? [];

    public void SetFilter(AppFilter filter) => FilterChoice.SelectedIndex = (int)filter;

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
        if (!_rows.TryGetValue(appId, out var row) || !Rows.Contains(row))
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
        RefreshLabel.Text = "Suche …";
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
            RefreshLabel.Text = "Nach Updates suchen";
        }
    }

    /// <summary>Asks the business layer for the list of the toolbar and shows it; the chosen app stays chosen.</summary>
    public void Refresh()
    {
        var selected = AppList.SelectedItem as AppRow;
        var statuses = AppService.Query(new AppQuery(SearchBox.Text ?? "", Filter, (AppSort)Math.Max(0, SortChoice.SelectedIndex)));
        var rows = statuses.Select(status => _rows[status.App.Id]).ToList();
        foreach (var status in statuses)
        {
            _rows[status.App.Id].Show(status);
        }

        if (!rows.SequenceEqual(Rows))
        {
            AppList.ItemsSource = rows;
            AppList.SelectedItem = selected is not null && rows.Contains(selected) ? selected : rows.FirstOrDefault();
        }

        EmptyState.IsVisible = rows.Count == 0;
        EmptyText.Text = (SearchBox.Text ?? "").Trim().Length > 0
            ? $"Keine App passt zu „{SearchBox.Text!.Trim()}“."
            : Filter switch
            {
                AppFilter.Installed => "Noch keine App installiert.",
                AppFilter.Updates => "Alles aktuell – keine Updates.",
                AppFilter.NotInstalled => "Alle Apps sind installiert.",
                _ => "Keine Apps.",
            };
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
        var parts = new List<string> { $"{all.Count} Apps", $"{installed} installiert" };
        if (updates > 0)
        {
            parts.Add(updates == 1 ? "1 Update" : $"{updates} Updates");
        }

        parts.Add($"Kanal {AppService.ChannelName}");
        parts.Add(AppService.IsRefreshing ? "wird geprüft …"
            : AppService.LastRefresh is { } last ? $"geprüft {Ui.FormatDateTime(last)}" : "noch nicht geprüft");
        SubtitleText.Text = string.Join(" · ", parts);
        TitleText.Text = Filter switch
        {
            AppFilter.Installed => "Installierte Apps",
            AppFilter.Updates => "Updates",
            AppFilter.NotInstalled => "Nicht installiert",
            _ => "Apps",
        };
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

    private async void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } status)
        {
            await RunAsync(AppAction.Uninstall, status);
        }
    }
}
