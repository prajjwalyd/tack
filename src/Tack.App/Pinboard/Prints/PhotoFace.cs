using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Tack.Core.Diagnostics;
using Tack.Core.Pinboard;
using Tack.Core.Thumbnails;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// A print's photo inside its paper border: the thumbnail filling the box
/// (cropped to it), decoded off the UI thread at the size it is drawn; a
/// hairline where photo meets paper, so light screenshots keep their edge;
/// and the sheen, a diagonal band of light that follows the pointer while hovered.
/// </summary>
sealed class PhotoFace
{
    /// <summary>The sheen band is this much larger than the photo, and the pointer moves it this share of its size.</summary>
    const double SheenSize = 1.6;
    const double SheenTravel = 0.16;
    const double SheenAngle = 118;

    readonly Image _picture = new() { Stretch = Stretch.UniformToFill };
    readonly Border _hairline = new() { CornerRadius = new CornerRadius(BoardMetrics.PhotoRadius), IsHitTestVisible = false };
    readonly LinearGradientBrush _band;
    readonly Border _sheen;
    readonly Canvas _sheenBox = new() { IsHitTestVisible = false };
    byte[]? _jpeg;
    double _aspect = 1;
    int _decoded;
    double _w;
    double _h;
    double _scale = 1;

    public PhotoFace()
    {
        static Color White(double a) => BoardTheme.Rgba(255, 255, 255, a);
        _band = AngledGradient.Make(SheenAngle, 1, 1,
            (White(0), 0.28), (White(0.07), 0.40), (White(0.26), 0.49), (White(0.10), 0.56), (White(0), 0.68));
        _sheen = new Border { Background = _band, IsHitTestVisible = false };
        ElementCompositionPreview.SetIsTranslationEnabled(_sheen, true);
        Sheen = ElementCompositionPreview.GetElementVisual(_sheen);
        _sheenBox.Children.Add(_sheen);
        Root = new Grid
        {
            CornerRadius = new CornerRadius(BoardMetrics.PhotoRadius),
            Margin = new Thickness(BoardMetrics.PrintBorder),
            Children = { _picture, _hairline, _sheenBox },
        };
    }

    /// <summary>The photo, inset by the paper border.</summary>
    public Grid Root { get; }

    /// <summary>The thumbnail itself: a refreshed picture flashes in on it.</summary>
    public Image Picture => _picture;

    /// <summary>The sheen band: its opacity, and its translation following the pointer.</summary>
    public Visual Sheen { get; }

    /// <summary>Where the sheen sits for the pointer at (<paramref name="nx"/>, <paramref name="ny"/>), each -1 to 1 across the print.</summary>
    public (double X, double Y) SheenAt(double nx, double ny) =>
        (nx * SheenTravel * SheenSize * _w, ny * SheenTravel * SheenSize * _h);

    /// <summary>Shows <paramref name="thumb"/>; none shows the empty photo colour.</summary>
    public void Show(Thumb? thumb)
    {
        if (ReferenceEquals(thumb?.Jpeg, _jpeg))
        {
            return;
        }
        _jpeg = thumb?.Jpeg;
        _aspect = thumb is { Width: > 0, Height: > 0 } ? thumb.Width / (double)thumb.Height : 1;
        _decoded = 0;
        if (_jpeg is null)
        {
            _picture.Source = null;
        }
        Decode();
    }

    /// <summary>The photo's size, logical px: the print less its paper border.</summary>
    public void Resize(double w, double h)
    {
        (_w, _h) = (w, h);
        _sheenBox.Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, h) };
        (_sheen.Width, _sheen.Height) = (w * SheenSize, h * SheenSize);
        Canvas.SetLeft(_sheen, -w * (SheenSize - 1) / 2);
        Canvas.SetTop(_sheen, -h * (SheenSize - 1) / 2);
        AngledGradient.Aim(_band, SheenAngle, _sheen.Width, _sheen.Height);
        Decode();
    }

    /// <summary>The screen's scale: a larger one decodes the thumbnail again, sharper.</summary>
    public void Rescale(double scale)
    {
        _scale = scale;
        Decode();
    }

    public void ApplyTheme(BoardTheme theme)
    {
        Root.Background = theme.Brush(theme.IsHighContrast ? theme.Canvas : theme.PhotoEmpty);
        _hairline.BorderThickness = new Thickness(theme.IsHighContrast ? 1 : 0.5);
        _hairline.BorderBrush = theme.Brush(theme.IsHighContrast ? theme.CanvasText : BoardTheme.Rgba(0, 0, 0, 0.14));
    }

    /// <summary>Decodes the thumbnail to the pixels it covers (cropped to fill), unless it already has as many.</summary>
    void Decode()
    {
        if (_jpeg is not { } jpeg || _w <= 0)
        {
            return;
        }
        var width = (int)Math.Ceiling(Math.Max(_w, _h * _aspect) * _scale);
        if (width <= _decoded)
        {
            return;
        }
        _decoded = width;
        _ = LoadAsync(jpeg, width);
    }

    async Task LoadAsync(byte[] jpeg, int width)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(jpeg.AsBuffer());
            stream.Seek(0);
            var picture = new BitmapImage { DecodePixelType = DecodePixelType.Physical, DecodePixelWidth = width };
            await picture.SetSourceAsync(stream);
            if (ReferenceEquals(jpeg, _jpeg) && width == _decoded)
            {
                _picture.Source = picture;
            }
        }
        catch (Exception e)
        {
            DevTrace.Write($"print: the thumbnail could not be shown: {e.Message}");
        }
    }
}
