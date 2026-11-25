using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Controls;

/// <summary>
/// The notable features of an app (<see cref="ThixApp.Features"/>: Cloud, MCP) as small chips side by side – icon and word in the
/// accent's text colour on its soft tint, what the feature means as the tool tip. Hidden when the app has none. On a picture
/// (<see cref="IsOnPicture"/>, the band of a tile) a chip sits on the page's surface with a border so it stands out from any
/// picture. Colours come from MatDesktop's theme and follow light and dark.
/// </summary>
public sealed class FeatureChips : StackPanel
{
    private IReadOnlyList<AppFeature> _features = [];
    private bool _isOnPicture;

    public FeatureChips()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        VerticalAlignment = VerticalAlignment.Center;
        IsVisible = false;
    }

    /// <summary>The chips sit on a picture: an opaque surface with a border instead of the soft tint.</summary>
    public bool IsOnPicture
    {
        get => _isOnPicture;
        set
        {
            if (_isOnPicture == value)
            {
                return;
            }

            _isOnPicture = value;
            Build();
        }
    }

    /// <summary>The features shown.</summary>
    public IReadOnlyList<AppFeature> Features => _features;

    /// <summary>The words on the chips (tests).</summary>
    public IReadOnlyList<string> Texts => _features.Select(AppFeatures.Name).ToList();

    /// <summary>The chips, one per feature (tests: their tool tips).</summary>
    public IReadOnlyList<Border> Chips => Children.OfType<Border>().ToList();

    /// <summary>Shows the features of <paramref name="app"/>; builds the chips only when they differ from the ones shown.</summary>
    public void Show(ThixApp app)
    {
        if (_features.SequenceEqual(app.Features))
        {
            return;
        }

        _features = app.Features;
        Build();
    }

    private void Build()
    {
        Children.Clear();
        foreach (var feature in _features)
        {
            Children.Add(Chip(feature));
        }

        IsVisible = _features.Count > 0;
    }

    private Border Chip(AppFeature feature)
    {
        var icon = new MatIcon { Icon = AppFeatures.Icon(feature), Size = 12 }.Use(MatIcon.ForegroundProperty, "Mat.AccentText");
        var text = new TextBlock
        {
            Text = AppFeatures.Name(feature),
            FontSize = MatTokens.Type.FsXs,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        }.Use(TextBlock.ForegroundProperty, "Mat.AccentText");
        var chip = new Border
        {
            Padding = new Thickness(7, 2, 8, 2),
            CornerRadius = new CornerRadius(999),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { icon, text } },
        };
        if (IsOnPicture)
        {
            chip.BorderThickness = new Thickness(1);
            chip.Use(Border.BackgroundProperty, "Mat.Surface").Use(Border.BorderBrushProperty, "Mat.Border");
        }
        else
        {
            chip.Use(Border.BackgroundProperty, "Mat.AccentSoft");
        }

        var description = AppFeatures.Description(feature);
        ToolTip.SetTip(chip, description);
        AutomationProperties.SetName(chip, $"{AppFeatures.Name(feature)}: {description}");
        return chip;
    }
}
