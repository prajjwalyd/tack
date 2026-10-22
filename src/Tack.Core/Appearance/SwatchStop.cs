using Tack.Core.Pinboard.Look;

namespace Tack.Core.Appearance;

/// <summary>A colour at a point along a swatch's gradient, from 0 (its start) to 1 (its end).</summary>
public readonly record struct SwatchStop(double At, Rgba Colour)
{
    /// <summary>
    /// The colour at <paramref name="at"/> along <paramref name="stops"/> (in
    /// order of <see cref="At"/>): mixed evenly between the stops either side,
    /// the end's own colour past either end.
    /// </summary>
    public static Rgba Sample(IReadOnlyList<SwatchStop> stops, double at)
    {
        if (at <= stops[0].At)
        {
            return stops[0].Colour;
        }
        for (var n = 1; n < stops.Count; n++)
        {
            var (from, to) = (stops[n - 1], stops[n]);
            if (at <= to.At)
            {
                var t = to.At > from.At ? (at - from.At) / (to.At - from.At) : 1;
                return new Rgba(
                    Lerp(from.Colour.Rgb.R, to.Colour.Rgb.R, t),
                    Lerp(from.Colour.Rgb.G, to.Colour.Rgb.G, t),
                    Lerp(from.Colour.Rgb.B, to.Colour.Rgb.B, t),
                    Lerp(from.Colour.A, to.Colour.A, t));
            }
        }
        return stops[^1].Colour;
    }

    static double Lerp(double from, double to, double t) => from + (to - from) * t;
}
