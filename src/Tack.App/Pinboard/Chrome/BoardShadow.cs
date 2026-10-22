using System.Numerics;
using Microsoft.UI.Composition;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;
using Windows.UI;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// What the board casts and its outline, under it: a wide soft shadow below
/// (14 px down, 34 px blur, drawn in 6 px from the board's sides), a tight one
/// just under its edge (1 px down, 2 px blur), and a 1 px dark ring round it.
/// The wide one is a blurred rectangle, which is the product of a blurred
/// edge across and a blurred edge down: one sprite whose colour fades down
/// and whose mask fades at the sides, rebuilt when the board's width changes.
/// The tight one and the ring follow the rounded corners. All of it reaches up
/// past the board's top, out of sight above the screen.
/// </summary>
sealed class BoardShadow
{
    const double Height = BoardMetrics.ElementHeight;
    const double Radius = BoardMetrics.BoardRadius;

    /// <summary>How far up past the board's top the outline runs, so its top corners never show.</summary>
    const float Above = 40;

    const double WideSigma = 34 / 2.0;
    const double WideDrop = 14;
    const double WideInset = 6;
    const double Reach = 3 * WideSigma;

    /// <summary>The tight shadow's strength in the one pixel it shows beyond the ring.</summary>
    const double TightShare = 0.31;

    readonly CompositionLinearGradientBrush _fall;
    readonly CompositionLinearGradientBrush _sides;
    readonly SpriteVisual _wide;
    readonly ShapeVisual _outline;
    readonly CompositionRoundedRectangleGeometry _tightShape;
    readonly CompositionRoundedRectangleGeometry _ringShape;
    readonly CompositionColorBrush _tight;
    readonly CompositionColorBrush _ring;
    double _width;

    public BoardShadow(Compositor compositor)
    {
        Visual = compositor.CreateContainerVisual();
        _fall = Gradients.Down(compositor);
        _sides = Gradients.Across(compositor);
        var brush = compositor.CreateMaskBrush();
        (brush.Source, brush.Mask) = (_fall, _sides);
        _wide = compositor.CreateSpriteVisual();
        _wide.Brush = brush;
        _wide.Offset = new Vector3((float)(WideInset - Reach), 0, 0);
        Visual.Children.InsertAtTop(_wide);

        _outline = compositor.CreateShapeVisual();
        _outline.Offset = new Vector3(-4, -Above, 0);
        (_tight, _ring) = (compositor.CreateColorBrush(), compositor.CreateColorBrush());
        // The tight shadow, 1 px wider all round and 1 px down: beyond the ring it shows only below.
        _tightShape = compositor.CreateRoundedRectangleGeometry();
        _tightShape.CornerRadius = new Vector2((float)Radius + 1);
        _tightShape.Offset = new Vector2(4 - 1, Above / 2 + 1);
        var tight = compositor.CreateSpriteShape(_tightShape);
        tight.FillBrush = _tight;
        // The ring: a 1 px stroke along the middle of the pixel outside the board.
        _ringShape = compositor.CreateRoundedRectangleGeometry();
        _ringShape.CornerRadius = new Vector2((float)Radius + 0.5f);
        _ringShape.Offset = new Vector2(4 - 0.5f, Above / 2);
        var ring = compositor.CreateSpriteShape(_ringShape);
        (ring.StrokeBrush, ring.StrokeThickness) = (_ring, 1);
        _outline.Shapes.Add(tight);
        _outline.Shapes.Add(ring);
        Visual.Children.InsertAtTop(_outline);
    }

    public ContainerVisual Visual { get; }

    public void Paint(bool dark)
    {
        var tight = dark ? new Rgba(0, 0, 0, 0.5) : new Rgba(10, 20, 16, 0.25);
        var wide = dark ? new Rgba(0, 0, 0, 0.55) : new Rgba(10, 24, 18, 0.30);
        _tight.Color = tight.Times(TightShare).ToColor();
        _ring.Color = (dark ? new Rgba(0, 0, 0, 0.65) : new Rgba(20, 28, 34, 0.38)).ToColor();
        // Down the sprite: full strength until the blurred bottom edge, 14 px below the board's, less the inset.
        var bottom = Height + WideDrop - WideInset;
        var length = bottom + Reach;
        Gradients.SetStops(_fall, Gradients.Profile(wide.ToColor(), [0, .. Gradients.Steps(bottom - Reach, length, 13)], length,
            y => Blur.Cover(bottom - y, WideSigma)));
        Resize(_width);
    }

    public void Resize(double width)
    {
        _width = width;
        if (width <= 0)
        {
            return;
        }
        var w = (float)width;
        _tightShape.Size = new Vector2(w + 2, (float)Height + Above / 2 + 1);
        _ringShape.Size = new Vector2(w + 1, (float)Height + Above / 2 + 0.5f);
        _outline.Size = new Vector2(w + 8, (float)Height + Above + 4);
        // Across the sprite: the shape runs from Reach to (its width - Reach), its sides blurred.
        var length = width - 2 * WideInset + 2 * Reach;
        _wide.Size = new Vector2((float)length, (float)(Height + WideDrop - WideInset + Reach));
        double Cover(double x) => Blur.Cover(x - Reach, WideSigma) - Blur.Cover(x - (length - Reach), WideSigma);
        var points = Gradients.Steps(0, 2 * Reach, 13).Concat(Gradients.Steps(length - 2 * Reach, length, 13));
        Gradients.SetStops(_sides, Gradients.Profile(Color.FromArgb(255, 255, 255, 255), points, length, Cover));
    }
}
