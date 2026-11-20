using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Controls;

/// <summary>
/// The last tile (or row) of the library: "Apps hinzufügen" – a click, Enter or a double click opens the store. It looks like an
/// app's tile (the same size, the band on top) with a big plus instead of a cover.
/// </summary>
public sealed class AddAppsCard : Decorator
{
    public AddAppsCard(AppsView view)
    {
        AutomationProperties.SetName(this, "Apps hinzufügen");
        ToolTip.SetTip(this, "Weitere Thix-Apps im Store");
        ShowView(view);
    }

    public AppsView View { get; private set; }

    public void ShowView(AppsView view)
    {
        View = view;
        var title = new TextBlock { Text = "Apps hinzufügen", FontWeight = FontWeight.SemiBold, FontSize = MatTokens.Type.FsLg };
        var line = new TextBlock { Text = "Alle Thix-Apps im Store – mit einem Klick installiert", TextWrapping = TextWrapping.Wrap }
            .Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        if (view == AppsView.Tiles)
        {
            var radius = MatTokens.Size.RadiusLg - 1;
            var band = new Border
            {
                Height = 150,
                CornerRadius = new CornerRadius(radius, radius, 0, 0),
                Child = Plus(64, 30),
            };
            band.Classes.Add("app-band");
            title.HorizontalAlignment = HorizontalAlignment.Center;
            line.TextAlignment = TextAlignment.Center;
            var texts = new StackPanel { Margin = new Thickness(16, 12, 16, 10), Spacing = 4, Children = { title, line } };
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { band, texts } };
            Grid.SetRow(texts, 1);
            Child = grid;
            return;
        }

        var plus = Plus(60, 26);
        plus.Margin = new Thickness(0, 0, 16, 0);
        var rowTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, line } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { plus, rowTexts } };
        Grid.SetColumn(rowTexts, 1);
        Child = row;
    }

    /// <summary>A round plus in the tint of the accent.</summary>
    private static Border Plus(double size, double iconSize) => new Border
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new MatIcon { Icon = "plus", Size = iconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .Use(MatIcon.ForegroundProperty, "Mat.AccentText"),
    }.Use(Border.BackgroundProperty, "Mat.AccentSoft");
}
