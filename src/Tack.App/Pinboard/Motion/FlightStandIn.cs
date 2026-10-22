using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Tack.App.Pinboard.Model;
using Tack.App.Pinboard.Prints;
using Tack.Core.Geometry;
using Tack.Core.Pinboard;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// What flies in for a new capture, on the overlay over the window: laid out
/// where the print will rest and turned and scaled about the pin point. Its
/// lift shadow and its paper fade in on the way; the picture, inside the
/// paper border, is stretched to the box, so at the start it covers the snip
/// exactly; it carries a copy of the print's pin. It never takes the pointer.
/// </summary>
sealed class FlightStandIn
{
    readonly Image _photo = new() { Stretch = Stretch.Fill };

    public FlightStandIn(BoardContext board, BoardItem item, LogicalRect rest, ImageSource? picture)
    {
        var theme = board.Theme;
        var (w, h) = (rest.W, rest.H);
        var art = PrintArt.ScaleFor(board.State.Scale);
        var contrast = theme.IsHighContrast;

        var lift = new NineGridShadow();
        lift.Resize(w, h);
        lift.Show(contrast ? null : ShadowArt.Lift, theme.IsDark, art);
        lift.Element.RenderTransform = new CompositeTransform { TranslateY = 4, ScaleX = 1.03, ScaleY = 1.03, CenterX = w / 2, CenterY = h / 2 };

        var restShadow = new NineGridShadow();
        restShadow.Resize(w, h);
        restShadow.Show(contrast ? null : ShadowArt.Rest, theme.IsDark, art);
        var paper = new Grid
        {
            Width = w,
            Height = h,
            Children =
            {
                restShadow.Element,
                new Border
                {
                    CornerRadius = new CornerRadius(BoardMetrics.PrintRadius),
                    Background = theme.Brush(contrast ? theme.Canvas : theme.Paper),
                    BorderBrush = contrast ? theme.Brush(theme.CanvasText) : null,
                    BorderThickness = new Thickness(contrast ? 1 : 0),
                },
            },
        };

        var border = BoardMetrics.PrintBorder;
        var photo = new Border
        {
            Width = Math.Max(1, w - 2 * border),
            Height = Math.Max(1, h - 2 * border),
            Background = theme.Brush(contrast ? theme.Canvas : theme.PhotoEmpty),
            Child = _photo,
        };
        Canvas.SetLeft(photo, border);
        Canvas.SetTop(photo, border);
        _photo.Source = picture;

        var pin = new PinArt();
        pin.Show(item.PinColor, theme.IsDark, art);
        var size = BoardMetrics.PinSize;
        Canvas.SetLeft(pin.Root, (w - size) / 2);
        Canvas.SetTop(pin.Root, BoardMetrics.PinY - size / 2);
        ElementCompositionPreview.GetElementVisual(pin.Brass).Opacity = item.Print.Kept ? 1 : 0;
        ElementCompositionPreview.GetElementVisual(pin.GlintBand).Opacity = 0;
        pin.Body.Scale = item.Print.Kept ? new Vector3((float)BoardMetrics.KeptPinScale, (float)BoardMetrics.KeptPinScale, 1) : Vector3.One;

        Root = new Canvas { Width = w, Height = h, IsHitTestVisible = false, Children = { lift.Element, paper, photo, pin.Root } };
        Canvas.SetLeft(Root, rest.X);
        Canvas.SetTop(Root, rest.Y);
        ElementCompositionPreview.SetIsTranslationEnabled(Root, true);
        ElementCompositionPreview.SetIsTranslationEnabled(pin.Root, true);
        Visual = ElementCompositionPreview.GetElementVisual(Root);
        Visual.CenterPoint = new Vector3((float)(w / 2), (float)BoardMetrics.PinY, 0);
        Shade = ElementCompositionPreview.GetElementVisual(lift.Element);
        Paper = ElementCompositionPreview.GetElementVisual(paper);
        Pin = ElementCompositionPreview.GetElementVisual(pin.Root);
        Pin.CenterPoint = new Vector3((float)(size / 2), (float)(size / 2), 0);
        Shade.Opacity = Paper.Opacity = 0;
    }

    /// <summary>The whole stand-in, to be put on the overlay.</summary>
    public Canvas Root { get; }

    /// <summary>The stand-in's transform and opacity: the flight's frames.</summary>
    public Visual Visual { get; }

    public Visual Shade { get; }

    public Visual Paper { get; }

    public Visual Pin { get; }

    /// <summary>Lets go of the picture, so its decoded pixels are freed now.</summary>
    public void Release() => _photo.Source = null;
}
