using System.Numerics;
using Microsoft.UI.Composition;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// A fine shade along the rails' inner edge, so the felt sits below the
/// frame: a half-pixel hairline all round, a soft shade 1 px in from each
/// side (1.5 px blur, drawn in by half a pixel) and one 1 px up from the
/// bottom (2 px blur). Each shade also reaches faintly along the edges it is
/// not cast from. Three strips of gradient, over the caps, clipped as the felt is.
/// </summary>
sealed class InnerShade
{
    const double Height = BoardMetrics.ElementHeight - BoardMetrics.FrameWidth;

    /// <summary>How far in a strip runs: past it the shade is gone.</summary>
    const double Depth = 3;

    const double Hairline = 0.5;
    const double SideSigma = 1.5 / 2;
    const double BottomSigma = 2 / 2.0;

    static readonly double[] Points = [0, Hairline - 1e-3, Hairline, 1, 1.5, 2, 2.5, Depth];

    readonly RoundedClip _clip;
    readonly CompositionLinearGradientBrush _leftBrush;
    readonly CompositionLinearGradientBrush _rightBrush;
    readonly CompositionLinearGradientBrush _bottomBrush;
    readonly SpriteVisual _left;
    readonly SpriteVisual _right;
    readonly SpriteVisual _bottom;

    public InnerShade(Compositor compositor)
    {
        Visual = compositor.CreateContainerVisual();
        Visual.Offset = new Vector3((float)BoardMetrics.FrameWidth, 0, 0);
        _clip = new RoundedClip(compositor, BoardMetrics.BoardRadius - BoardMetrics.FrameWidth);
        Visual.Clip = _clip.Clip;
        _leftBrush = Gradients.Across(compositor);
        _rightBrush = Gradients.Linear(compositor, Vector2.UnitX, Vector2.Zero);
        _bottomBrush = Gradients.Linear(compositor, Vector2.UnitY, Vector2.Zero);
        (_left, _right, _bottom) = (Sprite(compositor, _leftBrush), Sprite(compositor, _rightBrush), Sprite(compositor, _bottomBrush));
    }

    public ContainerVisual Visual { get; }

    public void Paint(Rgba shade)
    {
        var a = shade.A;
        var colour = shade.Rgb.ToColor();
        // Shades of one colour laid over each other: what shows is one less what each lets through.
        double Hair(double d) => d < Hairline ? a : 0;
        double Side(double d) => 1 - (1 - Hair(d))
            * (1 - a * Blur.Cover(0.5 - d, SideSigma))
            * (1 - a * Blur.Cover(-0.5 - d, BottomSigma));
        double Bottom(double d) => 1 - (1 - Hair(d))
            * (1 - a * Blur.Cover(0.5 - d, BottomSigma))
            * Math.Pow(1 - a * Blur.Cover(-0.5 - d, SideSigma), 2);
        var side = Gradients.Profile(colour, Points, Depth, Side).ToArray();
        Gradients.SetStops(_leftBrush, side);
        Gradients.SetStops(_rightBrush, side);
        Gradients.SetStops(_bottomBrush, Gradients.Profile(colour, Points, Depth, Bottom));
    }

    /// <summary>For a board <paramref name="boardWidth"/> wide.</summary>
    public void Resize(double boardWidth)
    {
        var width = (float)(boardWidth - 2 * BoardMetrics.FrameWidth);
        _clip.Resize(width, Height);
        _left.Size = _right.Size = new Vector2((float)Depth, (float)Height);
        _right.Offset = new Vector3(width - (float)Depth, 0, 0);
        _bottom.Size = new Vector2(width, (float)Depth);
        _bottom.Offset = new Vector3(0, (float)(Height - Depth), 0);
    }

    SpriteVisual Sprite(Compositor compositor, CompositionBrush brush)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = brush;
        Visual.Children.InsertAtTop(sprite);
        return sprite;
    }
}
