using Tack.Core.Geometry;

namespace Tack.Core.Snips;

/// <summary>A picture, point-sampled on the comparison grid: the screen is read at the same positions to compare.</summary>
public sealed class Samples
{
    /// <summary>Samples per side of the comparison grid.</summary>
    public const int Grid = 32;

    Samples(int w, int h, List<(int X, int Y)> points, List<Rgb> colours, double detail) =>
        (W, H, Points, Colours, Detail) = (w, h, points, colours, detail);

    public int W { get; }

    public int H { get; }

    internal IReadOnlyList<(int X, int Y)> Points { get; }

    internal IReadOnlyList<Rgb> Colours { get; }

    /// <summary>How much the samples differ from their average: 0 for a flat colour, whose match says little.</summary>
    public double Detail { get; }

    /// <summary>Samples a <paramref name="w"/> x <paramref name="h"/> picture through <paramref name="get"/>.</summary>
    public static Samples Of(int w, int h, Func<int, int, Rgb> get) => Within(w, h, new Rect(0, 0, w, h), get);

    /// <summary>Samples only <paramref name="region"/>, in the picture's coordinates, of a <paramref name="w"/> x <paramref name="h"/> picture.</summary>
    public static Samples Within(int w, int h, Rect region, Func<int, int, Rgb> get)
    {
        List<(int X, int Y)> points = [.. GridPoints(Math.Max(region.W, 0), Math.Max(region.H, 0))
            .Select(p => (X: p.X + region.X, Y: p.Y + region.Y))
            .Where(p => p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h)];
        List<Rgb> colours = [.. points.Select(p => get(p.X, p.Y))];
        var n = Math.Max(colours.Count, 1);
        var (r, g, b) = (colours.Sum(c => (double)c.R) / n, colours.Sum(c => (double)c.G) / n, colours.Sum(c => (double)c.B) / n);
        var detail = colours.Sum(c => Math.Abs(c.R - r) + Math.Abs(c.G - g) + Math.Abs(c.B - b)) / n;
        return new Samples(w, h, points, colours, detail);
    }

    /// <summary>
    /// The grid's positions in a <paramref name="w"/> x <paramref name="h"/> picture:
    /// the centres of a <see cref="Grid"/> x <see cref="Grid"/> split, row by row,
    /// so none lies on an edge, where Snipping Tool may still be drawing its outline.
    /// </summary>
    public static List<(int X, int Y)> GridPoints(int w, int h)
    {
        var points = new List<(int X, int Y)>();
        if (w <= 0 || h <= 0)
        {
            return points;
        }
        static int At(int i, int n) => (int)((2L * i + 1) * n / (2L * Grid));
        for (var j = 0; j < Grid; j++)
        {
            for (var i = 0; i < Grid; i++)
            {
                points.Add((At(i, w), At(j, h)));
            }
        }
        return points;
    }
}
