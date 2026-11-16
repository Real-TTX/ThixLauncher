using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;

namespace ThixLauncher.Controls;

/// <summary>
/// The logo of an app of the catalog at a size (its own tile, Assets/apps/&lt;id&gt;.png). An app without a logo gets a tile in
/// the accent tint with its icon of the sprite. Several on one page are independent (no shared state).
/// </summary>
public sealed class AppLogo : Decorator
{
    public static readonly StyledProperty<string?> AppIdProperty = AvaloniaProperty.Register<AppLogo, string?>(nameof(AppId));

    public static readonly StyledProperty<string?> FallbackIconProperty = AvaloniaProperty.Register<AppLogo, string?>(nameof(FallbackIcon), "apps");

    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<AppLogo, double>(nameof(Size), 56);

    static AppLogo()
    {
        AppIdProperty.Changed.AddClassHandler<AppLogo>((logo, _) => logo.Refresh());
        FallbackIconProperty.Changed.AddClassHandler<AppLogo>((logo, _) => logo.Refresh());
        SizeProperty.Changed.AddClassHandler<AppLogo>((logo, _) => logo.Refresh());
    }

    public AppLogo()
    {
        VerticalAlignment = VerticalAlignment.Center;
        Refresh();
    }

    /// <summary>The id of the app in the catalog ("thixclip").</summary>
    public string? AppId
    {
        get => GetValue(AppIdProperty);
        set => SetValue(AppIdProperty, value);
    }

    /// <summary>The icon of the sprite when the app has no logo.</summary>
    public string? FallbackIcon
    {
        get => GetValue(FallbackIconProperty);
        set => SetValue(FallbackIconProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Refresh()
    {
        Width = Height = Size;
        if ((AppId is null ? null : Ui.AppLogo(AppId)) is { } logo)
        {
            var image = new Image { Source = logo, Width = Size, Height = Size };
            RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
            Child = image;
            return;
        }

        Child = new Border
        {
            CornerRadius = new CornerRadius(Size * 0.23),
            Child = new MatIcon { Icon = FallbackIcon ?? "apps", Size = Size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                .Use(MatIcon.ForegroundProperty, "Mat.AccentText"),
        }.Use(Border.BackgroundProperty, "Mat.AccentSoft");
    }
}
