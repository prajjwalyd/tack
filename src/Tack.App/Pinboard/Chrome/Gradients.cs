using System.Numerics;
using Microsoft.UI.Composition;
using Windows.UI;

namespace Tack.App.Pinboard.Chrome;

/// <summary>Gradient brushes for the chrome: made once, their stops replaced when the look changes.</summary>
static class Gradients
{
    /// <summary>A gradient along <paramref name="from"/> to <paramref name="to"/>, as fractions of the visual it paints.</summary>
    public static CompositionLinearGradientBrush Linear(Compositor compositor, Vector2 from, Vector2 to)
    {
        var brush = compositor.CreateLinearGradientBrush();
        (brush.StartPoint, brush.EndPoint) = (from, to);
        return brush;
    }

    /// <summary>Top to bottom over the visual it paints.</summary>
    public static CompositionLinearGradientBrush Down(Compositor compositor) => Linear(compositor, Vector2.Zero, Vector2.UnitY);

    /// <summary>Left to right over the visual it paints.</summary>
    public static CompositionLinearGradientBrush Across(Compositor compositor) => Linear(compositor, Vector2.Zero, Vector2.UnitX);

    /// <summary>Replaces the brush's stops: each a fraction along it (0 to 1) and a colour.</summary>
    public static void SetStops(CompositionGradientBrush brush, IEnumerable<(double At, Color Colour)> stops)
    {
        brush.ColorStops.Clear();
        foreach (var (at, colour) in stops)
        {
            brush.ColorStops.Add(brush.Compositor.CreateColorGradientStop((float)Math.Clamp(at, 0, 1), colour));
        }
    }

    /// <summary>
    /// A colour whose opacity follows <paramref name="cover"/> (0 to 1) at each of
    /// <paramref name="points"/>, placed at the point's fraction of <paramref name="length"/>.
    /// </summary>
    public static IEnumerable<(double, Color)> Profile(Color colour, IEnumerable<double> points, double length, Func<double, double> cover) =>
        points.Select(p => (p / length, Color.FromArgb((byte)Math.Round(Math.Clamp(cover(p), 0, 1) * colour.A), colour.R, colour.G, colour.B)));

    /// <summary><paramref name="count"/> points evenly from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    public static IEnumerable<double> Steps(double from, double to, int count) =>
        Enumerable.Range(0, count).Select(i => from + (to - from) * i / (count - 1));
}
