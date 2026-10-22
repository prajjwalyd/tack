using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tack.App.Pinboard.Chrome;
using Tack.Core.Appearance;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Tack.App.Settings.Sections.Appearance;

/// <summary>
/// A disc whose colour turns round its centre, from a gradient's stops: the
/// custom swatch's colour wheel and the brushed bands on a frame swatch. XAML
/// has no such brush, so the disc is thin slices, each the colour at its middle.
/// </summary>
static class ConicFill
{
    const int Slices = 90;

    /// <summary>
    /// A disc of <paramref name="size"/> px coloured by <paramref name="stops"/>
    /// clockwise from <paramref name="fromDegrees"/> (0 is the top).
    /// </summary>
    public static Canvas Disc(IReadOnlyList<SwatchStop> stops, double size, double fromDegrees = 0)
    {
        var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        var opaque = stops.All(s => s.Colour.A >= 1);
        var step = 360.0 / Slices;
        for (var n = 0; n < Slices; n++)
        {
            var colour = SwatchStop.Sample(stops, (n + 0.5) / Slices);
            if (colour.A <= 0)
            {
                continue;
            }
            // Opaque slices overlap a little, so no seam of the background shows between them.
            var sweep = opaque ? step * 1.5 : step;
            canvas.Children.Add(new Path
            {
                Data = Slice(size / 2, fromDegrees + n * step, sweep),
                Fill = new SolidColorBrush(colour.ToColor()),
            });
        }
        return canvas;
    }

    static PathGeometry Slice(double radius, double startDegrees, double sweepDegrees)
    {
        var centre = new Point(radius, radius);
        var figure = new PathFigure { StartPoint = centre, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment { Point = At(centre, radius, startDegrees) });
        figure.Segments.Add(new ArcSegment
        {
            Point = At(centre, radius, startDegrees + sweepDegrees),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>The point on the circle <paramref name="degrees"/> clockwise from the top.</summary>
    static Point At(Point centre, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Sin(radians), centre.Y - radius * Math.Cos(radians));
    }
}
