using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Controls;

/// <summary>
/// A picture of an app from its GitHub repository (<see cref="StoreService.PictureAsync"/>): the cover of a tile, the big
/// picture of the store and the gallery of the app's page. It loads in the background when it is shown and again when the
/// theme changes (light / dark picture); until then – or without a connection – the band colour with the app's logo (or
/// nothing, <see cref="ShowsLogo"/>) stands in. Several on one page are independent.
/// </summary>
public sealed class AppPicture : Border
{
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly AppLogo _logo = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private ThixApp? _app;
    private AppScreenshot? _picture;
    private int _loads;
    private bool _showsLogo = true;

    public AppPicture()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(_image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        _image.HorizontalAlignment = HorizontalAlignment.Center;
        _image.VerticalAlignment = VerticalAlignment.Top;
        Child = new Panel { Children = { _logo, _image } };
        ActualThemeVariantChanged += (_, _) => Load();
    }

    /// <summary>How the picture fills the control: <see cref="Stretch.UniformToFill"/> for covers (cut), Uniform for the gallery (whole).</summary>
    public Stretch Stretch
    {
        get => _image.Stretch;
        set
        {
            _image.Stretch = value;
            _image.VerticalAlignment = value == Stretch.UniformToFill ? VerticalAlignment.Top : VerticalAlignment.Center;
        }
    }

    /// <summary>The width the picture is decoded to (memory: a thumbnail does not need the whole screenshot).</summary>
    public int DecodeWidth { get; set; } = 640;

    /// <summary>The logo of the app stands in while there is no picture (off for covers that show the logo themselves).</summary>
    public bool ShowsLogo
    {
        get => _showsLogo;
        set
        {
            _showsLogo = value;
            _logo.IsVisible = value && _image.Source is null;
        }
    }

    public double LogoSize
    {
        get => _logo.Size;
        set => _logo.Size = value;
    }

    /// <summary>True when the picture is shown (tests).</summary>
    public bool HasPicture => _image.Source is not null;

    /// <summary>The picture shown (null: none).</summary>
    public AppScreenshot? Picture => _picture;

    /// <summary>Shows <paramref name="picture"/> of <paramref name="app"/> (loads it when the control is shown).</summary>
    public void Show(ThixApp? app, AppScreenshot? picture)
    {
        if (_app == app && _picture == picture)
        {
            return;
        }

        _app = app;
        _picture = picture;
        _logo.AppId = app?.Id;
        _logo.FallbackIcon = app?.Icon;
        Load();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Load();
    }

    private async void Load()
    {
        var load = ++_loads;
        if (_app is not { } app || _picture is not { } picture || VisualRoot is null)
        {
            if (_app is null || _picture is null)
            {
                _image.Source = null;
                _logo.IsVisible = _showsLogo;
            }

            return;
        }

        var path = await StoreService.PictureAsync(app, picture, ActualThemeVariant == ThemeVariant.Dark);
        var bitmap = path is null ? null : await Ui.PictureAsync(path, DecodeWidth);
        if (load == _loads)
        {
            _image.Source = bitmap;
            _logo.IsVisible = _showsLogo && bitmap is null;
        }
    }
}
