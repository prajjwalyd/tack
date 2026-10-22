using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The prints' line icons, drawn with round ends on their own small grids:
/// the unpin cross and the Keep bookmark (18), the copied check (28) and a
/// link note's chain link (10). Each call makes a new path.
/// </summary>
static class Icons
{
    /// <summary>The unpin button's cross, on an 18 px grid.</summary>
    public static Path Cross() => Stroke(18, 1.5,
        Figure((6.4, 6.4), false, Line(11.6, 11.6)),
        Figure((11.6, 6.4), false, Line(6.4, 11.6)));

    /// <summary>The Keep button's bookmark, on an 18 px grid; filled while kept.</summary>
    public static Path Keep() => Stroke(18, 1.5,
        Figure((6.6, 5.1), true,
            Line(11.4, 5.1), Arc(12.2, 5.9, 0.8, SweepDirection.Clockwise), Line(12.2, 13.1), Line(9, 11),
            Line(5.8, 13.1), Line(5.8, 5.9), Arc(6.6, 5.1, 0.8, SweepDirection.Clockwise)));

    /// <summary>The copied badge's check, on a 28 px grid.</summary>
    public static Path Check() => Stroke(28, 1.9,
        Figure((9.6, 14.4), false, Line(12.6, 17.4), Line(18.5, 10.8)));

    /// <summary>A link note's chain link, on a 10 px grid.</summary>
    public static Path Link() => Stroke(10, 1.1,
        Figure((4.3, 5.7), false, Line(5.7, 4.3)),
        Figure((3.6, 4.2), false, Line(2.5, 5.3), Arc(4.7, 7.5, 1.6, SweepDirection.Counterclockwise), Line(5.8, 6.4)),
        Figure((6.4, 5.8), false, Line(7.5, 4.7), Arc(5.3, 2.5, 1.6, SweepDirection.Counterclockwise), Line(4.2, 3.6)));

    static Path Stroke(double grid, double thickness, params PathFigure[] figures)
    {
        var geometry = new PathGeometry();
        foreach (var figure in figures)
        {
            geometry.Figures.Add(figure);
        }
        return new Path
        {
            Width = grid,
            Height = grid,
            Data = geometry,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
    }

    static PathFigure Figure((double X, double Y) start, bool closed, params PathSegment[] segments)
    {
        var figure = new PathFigure { StartPoint = new Point(start.X, start.Y), IsClosed = closed, IsFilled = closed };
        foreach (var segment in segments)
        {
            figure.Segments.Add(segment);
        }
        return figure;
    }

    static LineSegment Line(double x, double y) => new() { Point = new Point(x, y) };

    static ArcSegment Arc(double x, double y, double radius, SweepDirection sweep) =>
        new() { Point = new Point(x, y), Size = new Size(radius, radius), SweepDirection = sweep };
}
