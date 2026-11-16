using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Controls;

/// <summary>
/// One app in the list (a card row): its logo, the name with its line, the version or what is happening right now, the
/// state as a badge and the main action at the right (Öffnen, Installieren, Aktualisieren – like a store). While the app
/// is being changed a progress bar runs under the text. The row is built once and shows each new state (<see cref="Show"/>).
/// </summary>
public sealed class AppRow : Grid
{
    private readonly TextBlock _name = new() { FontSize = MatTokens.Type.FsLg, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _tagline = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _version = new() { FontSize = MatTokens.Type.FsSm, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
    private readonly ProgressBar _progress = new() { Maximum = 1, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
    private readonly StateBadge _badge = new() { Margin = new Thickness(16, 0, 16, 0) };
    private readonly Button _action = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 132 };
    private readonly IconLabel _actionLabel = new();

    public AppRow(ThixApp app)
    {
        App = app;
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto");
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

        var logo = new AppLogo { AppId = app.Id, FallbackIcon = app.Icon, Size = 56, Margin = new Thickness(0, 0, 16, 0) };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _name, _tagline, _version, _progress } };
        SetColumn(texts, 1);
        SetColumn(_badge, 2);
        SetColumn(_action, 3);
        Children.Add(logo);
        Children.Add(texts);
        Children.Add(_badge);
        Children.Add(_action);

        _name.Text = app.Name;
        _tagline.Text = app.Tagline;
        AutomationProperties.SetName(this, app.Name);
    }

    /// <summary>The main action of the row was clicked.</summary>
    public event EventHandler<AppAction>? ActionRequested;

    public ThixApp App { get; }

    /// <summary>The state shown now.</summary>
    public AppStatus? Status { get; private set; }

    /// <summary>The main action as shown now.</summary>
    public AppButton MainAction { get; private set; } = new(null, "", "");

    /// <summary>The words of the main action (tests).</summary>
    public string ActionText => _actionLabel.Text;

    public string VersionText => _version.Text ?? "";

    public StateBadge Badge => _badge;

    public void Show(AppStatus status)
    {
        Status = status;
        MainAction = AppActions.Primary(status);
        _version.Text = Ui.VersionLine(status);
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
}
