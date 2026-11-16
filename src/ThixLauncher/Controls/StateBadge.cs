using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Controls;

/// <summary>
/// The state of an app as a small pill in the tint of the state (installed green, update amber, working blue, else grey),
/// the words of <see cref="Ui.StateText"/>. Colours come from MatDesktop's theme and follow light and dark.
/// </summary>
public sealed class StateBadge : Border
{
    private readonly TextBlock _text = new() { FontSize = MatTokens.Type.FsXs, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public StateBadge()
    {
        Padding = new Thickness(10, 3);
        CornerRadius = new CornerRadius(999);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _text;
    }

    /// <summary>The words shown (tests).</summary>
    public string? Text => _text.Text;

    public void Show(AppStatus status)
    {
        var (background, foreground) = Ui.StateColors(status);
        this.Use(BackgroundProperty, background);
        _text.Use(TextBlock.ForegroundProperty, foreground);
        _text.Text = Ui.StateText(status);
    }
}
