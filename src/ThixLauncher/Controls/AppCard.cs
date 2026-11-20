using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Controls;

/// <summary>
/// One app of the library or the store, as a tile or as a row (<see cref="ShowView"/>): its logo, the name with its line, the
/// state as a badge, the version or what is happening right now, and the main action (Öffnen, Installieren, Aktualisieren –
/// like a store). The tile is a poster: the first picture of the app from GitHub as its cover (<see cref="AppPicture"/>) with
/// the logo on it – an app without pictures: the logo big on the band (class <c>app-band</c>, the page colours it with the
/// state of the tile) –, the name under it, the action across the foot. While the app is being changed a progress bar runs
/// (tile: at the foot of the band, row: under the text).
/// The card is built once and shows each new state (<see cref="Show"/>); both layouts use the same parts.
/// </summary>
public sealed class AppCard : Decorator
{
    private const double TileLogoSize = 88;
    private const double CoverLogoSize = 44;
    private const double RowLogoSize = 60;

    private readonly AppLogo _logo;
    private readonly AppPicture _cover = new() { ShowsLogo = false, DecodeWidth = 560 };
    private readonly TextBlock _name = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _tagline = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _version = new() { FontSize = MatTokens.Type.FsSm, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressBar _progress = new() { Maximum = 1, IsVisible = false };
    private readonly StateBadge _badge = new();
    private readonly Button _action = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 132 };
    private readonly IconLabel _actionLabel = new();

    public AppCard(ThixApp app, AppsView view)
    {
        App = app;
        _logo = new AppLogo { AppId = app.Id, FallbackIcon = app.Icon };
        _cover.Show(app, app.Screenshots.FirstOrDefault());
        _tagline.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _version.Use(TextBlock.ForegroundProperty, "Mat.TextFaint");
        _progress.Themed("MatProgress");
        _action.Content = _actionLabel;
        _action.Click += (_, e) =>
        {
            e.Handled = true;
            if (MainAction.Action is { } action)
            {
                ActionRequested?.Invoke(this, action);
            }
        };

        _name.Text = app.Name;
        _tagline.Text = app.Tagline;
        AutomationProperties.SetName(this, app.Name);
        ShowView(view);
    }

    /// <summary>The main action of the card was clicked.</summary>
    public event EventHandler<AppAction>? ActionRequested;

    public ThixApp App { get; }

    /// <summary>Tile or row.</summary>
    public AppsView View { get; private set; }

    /// <summary>The state shown now.</summary>
    public AppStatus? Status { get; private set; }

    /// <summary>The main action as shown now.</summary>
    public AppButton MainAction { get; private set; } = new(null, "", "");

    /// <summary>The words of the main action (tests).</summary>
    public string ActionText => _actionLabel.Text;

    /// <summary>The full version line (a tile shows it short, with this as its tool tip).</summary>
    public string VersionText { get; private set; } = "";

    public StateBadge Badge => _badge;

    /// <summary>The cover of the tile (the first picture of the app; tests).</summary>
    public AppPicture Cover => _cover;

    /// <summary>Lays the same parts out as a tile or as a row.</summary>
    public void ShowView(AppsView view)
    {
        View = view;
        foreach (var part in new Control[] { _logo, _cover, _name, _tagline, _version, _progress, _badge, _action })
        {
            Detach(part);
        }

        Child = view == AppsView.Tiles ? BuildTile() : BuildRow();
        ShowVersion();
    }

    public void Show(AppStatus status)
    {
        Status = status;
        MainAction = AppActions.Primary(status);
        VersionText = Ui.VersionLine(status);
        ShowVersion();
        _badge.Show(status);
        _progress.IsVisible = status.IsBusy;
        _progress.IsIndeterminate = status.Progress?.Fraction is null;
        _progress.Value = status.Progress?.Fraction ?? 0;

        _actionLabel.Icon = MainAction.Icon;
        _actionLabel.Text = MainAction.Text;
        _action.IsEnabled = MainAction.Action is not null;
        ToolTip.SetTip(_action, MainAction.Tip);
        AutomationProperties.SetName(_action, $"{MainAction.Text}: {status.App.Name}");
        if (MainAction.Action is AppAction.Install or AppAction.Update)
        {
            _action.Themed("MatButton.Primary");
        }
        else
        {
            _action.ClearValue(ThemeProperty);
        }
    }

    /// <summary>The tile: the cover with the logo on it (or the logo big on its band), name, line and state, the action across the foot.</summary>
    private Grid BuildTile()
    {
        var hasCover = App.Screenshots.Count > 0;
        _logo.Size = hasCover ? CoverLogoSize : TileLogoSize;
        _logo.Margin = default;
        _logo.HorizontalAlignment = HorizontalAlignment.Center;
        _name.FontSize = MatTokens.Type.FsLg;
        _name.TextAlignment = _tagline.TextAlignment = _version.TextAlignment = TextAlignment.Left;
        _name.HorizontalAlignment = _tagline.HorizontalAlignment = HorizontalAlignment.Stretch;
        _version.HorizontalAlignment = HorizontalAlignment.Left;
        _version.VerticalAlignment = VerticalAlignment.Center;
        _version.TextWrapping = TextWrapping.NoWrap;
        _version.MaxLines = 1;
        _version.Margin = new Thickness(10, 0, 0, 0);
        _badge.HorizontalAlignment = HorizontalAlignment.Left;
        _badge.Margin = default;
        _progress.MaxWidth = double.PositiveInfinity;
        _progress.HorizontalAlignment = HorizontalAlignment.Stretch;
        _progress.VerticalAlignment = VerticalAlignment.Bottom;
        _progress.Margin = hasCover ? new Thickness(0) : new Thickness(20, 0, 20, 12);
        _action.HorizontalAlignment = HorizontalAlignment.Stretch;
        _action.Margin = new Thickness(14, 0, 14, 14);

        var radius = MatTokens.Size.RadiusLg - 1;
        var bandContent = new Panel();
        if (hasCover)
        {
            var shade = new Border();
            shade.Classes.Add("cover-shade");
            var ring = new Border { Child = _logo, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12) };
            ring.Classes.Add("logo-ring");
            bandContent.Children.AddRange([_cover, shade, ring, _progress]);
        }
        else
        {
            bandContent.Children.AddRange([_logo, _progress]);
        }

        var band = new Border { Height = 150, CornerRadius = new CornerRadius(radius, radius, 0, 0), ClipToBounds = true, Child = bandContent };
        band.Classes.Add("app-band");
        var state = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { _badge, _version } };
        var texts = new StackPanel { Margin = new Thickness(16, 12, 16, 10), ClipToBounds = true, Children = { _name, _tagline, state } };
        var tile = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Children = { band, texts, _action } };
        Grid.SetColumn(texts, 0);
        Grid.SetColumn(_action, 0);
        Grid.SetRow(texts, 1);
        Grid.SetRow(_action, 2);
        return tile;
    }

    /// <summary>The row: logo, the texts, the state and the action at the right.</summary>
    private Grid BuildRow()
    {
        _logo.Size = RowLogoSize;
        _logo.Margin = new Thickness(0, 0, 16, 0);
        _logo.HorizontalAlignment = HorizontalAlignment.Left;
        _name.FontSize = MatTokens.Type.FsLg;
        _name.TextAlignment = _tagline.TextAlignment = _version.TextAlignment = TextAlignment.Left;
        _version.TextWrapping = TextWrapping.NoWrap;
        _version.MaxLines = 1;
        _version.Margin = new Thickness(0, 2, 0, 0);
        _badge.HorizontalAlignment = HorizontalAlignment.Left;
        _badge.Margin = new Thickness(16, 0, 16, 0);
        _progress.MaxWidth = 360;
        _progress.HorizontalAlignment = HorizontalAlignment.Left;
        _progress.VerticalAlignment = VerticalAlignment.Top;
        _progress.Margin = new Thickness(0, 8, 0, 0);
        _action.HorizontalAlignment = HorizontalAlignment.Right;
        _action.Margin = default;

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _name, _tagline, _version, _progress } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Children = { _logo, texts, _badge, _action } };
        Grid.SetRow(texts, 0);
        Grid.SetRow(_action, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(_badge, 2);
        Grid.SetColumn(_action, 3);
        return row;
    }

    private void ShowVersion()
    {
        if (Status is null)
        {
            return;
        }

        var isTile = View == AppsView.Tiles;
        _version.Text = isTile ? Ui.ShortVersionLine(Status) : VersionText;
        ToolTip.SetTip(_version, isTile && _version.Text != VersionText ? VersionText : null);
    }

    private static void Detach(Control part)
    {
        switch (part.Parent)
        {
            case Panel panel:
                panel.Children.Remove(part);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
        }
    }
}
