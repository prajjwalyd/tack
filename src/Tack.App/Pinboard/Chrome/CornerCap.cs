using System.Numerics;
using Microsoft.UI.Composition;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// A corner cap, a hair proud of the frame where a side rail meets the
/// bottom one: a quarter ring about (12, 2) from radius 5 to 12, run out
/// straight to the cap's top and right edges, its face shaded top to bottom,
/// with a lit rim on its inner curve, a shaded rim on its outer one, a dark
/// hairline along its flat edges and a soft shade cast on the felt inside the
/// curve. Drawn for the left corner; the right one is the same turned round.
/// </summary>
sealed class CornerCap
{
    const float Size = (float)BoardMetrics.CapSize;
    const float CentreX = 12;
    const float CentreY = 2;

    readonly Compositor _compositor;
    readonly CompositionLinearGradientBrush _face;
    readonly CompositionColorBrush _lit;
    readonly CompositionColorBrush _shaded;
    readonly CompositionColorBrush _edge;
    readonly CompositionColorBrush _shade;

    public CornerCap(Compositor compositor, bool mirrored)
    {
        _compositor = compositor;
        Visual = compositor.CreateContainerVisual();
        Visual.Size = new Vector2(Size);
        if (mirrored)
        {
            Visual.CenterPoint = new Vector3(Size / 2, Size / 2, 0);
            Visual.Scale = new Vector3(-1, 1, 1);
        }
        _face = compositor.CreateLinearGradientBrush();
        _face.MappingMode = CompositionMappingMode.Absolute;
        (_face.StartPoint, _face.EndPoint) = (Vector2.Zero, new Vector2(0.45f * Size, Size));
        (_lit, _shaded, _edge, _shade) = (compositor.CreateColorBrush(), compositor.CreateColorBrush(), compositor.CreateColorBrush(), compositor.CreateColorBrush());

        // The shade the cap casts: its inner curve moved down a little and softened.
        const float drop = 0.55f;
        const float soft = 1.4f;
        Layer(CentreY + drop, Arc(5, soft, _shade));
        Layer(null, Line(7, drop, 7, CentreY + drop, soft, _shade), Line(CentreX, 7 + drop, Size, 7 + drop, soft, _shade));
        // The face: the quarter ring as a thick circle seen only in its quadrant, and the two straight runs.
        Layer(CentreY, Arc(8.5f, 7, _face));
        Layer(null, Rect(0, 0, 7, CentreY + 0.5f), Rect(CentreX - 0.5f, 7, Size, Size));
        Layer(CentreY, Arc(5.25f, 0.5f, _lit), Arc(11.75f, 0.5f, _shaded));
        Layer(null,
            Line(6.75f, 0, 6.75f, CentreY, 0.5f, _lit), Line(CentreX, 7.25f, Size, 7.25f, 0.5f, _lit),
            Line(0.25f, 0, 0.25f, CentreY, 0.5f, _shaded), Line(CentreX, 13.75f, Size, 13.75f, 0.5f, _shaded),
            Line(0, 0.25f, 7, 0.25f, 0.5f, _edge), Line(13.75f, 7, 13.75f, Size, 0.5f, _edge));
    }

    public ContainerVisual Visual { get; }

    public void Paint(CapTones tones, CapShade shade)
    {
        Gradients.SetStops(_face, [(0, tones.Top.ToColor()), (1, tones.Bottom.ToColor())]);
        _lit.Color = tones.Lit.ToColor();
        _shaded.Color = tones.Shaded.ToColor();
        _edge.Color = new Rgba(0, 0, 0, shade.Edge).ToColor();
        // A 1.1 px stroke blurred by 0.45: about 0.78 of its strength, spread a little wider.
        _shade.Color = shade.Colour.At(shade.Opacity * 0.78).ToColor();
    }

    /// <summary>
    /// A layer of shapes in the cap's space; with <paramref name="quadrantTop"/>,
    /// seen only left of the curve's centre and below that height, where its arcs are.
    /// </summary>
    void Layer(float? quadrantTop, params CompositionShape[] shapes)
    {
        var layer = _compositor.CreateShapeVisual();
        layer.Size = new Vector2(Size);
        foreach (var shape in shapes)
        {
            layer.Shapes.Add(shape);
        }
        if (quadrantTop is { } top)
        {
            layer.Clip = _compositor.CreateInsetClip(0, top, Size - CentreX, 0);
        }
        Visual.Children.InsertAtTop(layer);
    }

    CompositionShape Arc(float radius, float thickness, CompositionBrush brush)
    {
        var circle = _compositor.CreateEllipseGeometry();
        circle.Center = new Vector2(CentreX, CentreY);
        circle.Radius = new Vector2(radius);
        var shape = _compositor.CreateSpriteShape(circle);
        (shape.StrokeBrush, shape.StrokeThickness) = (brush, thickness);
        return shape;
    }

    CompositionShape Line(float x1, float y1, float x2, float y2, float thickness, CompositionBrush brush)
    {
        var line = _compositor.CreateLineGeometry();
        (line.Start, line.End) = (new Vector2(x1, y1), new Vector2(x2, y2));
        var shape = _compositor.CreateSpriteShape(line);
        (shape.StrokeBrush, shape.StrokeThickness) = (brush, thickness);
        return shape;
    }

    CompositionShape Rect(float left, float top, float right, float bottom)
    {
        var rect = _compositor.CreateRectangleGeometry();
        rect.Offset = new Vector2(left, top);
        rect.Size = new Vector2(right - left, bottom - top);
        var shape = _compositor.CreateSpriteShape(rect);
        shape.FillBrush = _face;
        return shape;
    }
}
