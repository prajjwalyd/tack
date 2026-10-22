namespace Tack.Core.Pinboard.Layout;

/// <summary>
/// How a print hangs: a small resting tilt either way, chosen once when it
/// arrives, and the lean it is held at as it is pinned on, the same way as
/// its tilt. Degrees; the randomness is given, so tests can script it.
/// </summary>
public static class Tilt
{
    /// <summary>The pin-on's lean, either way, before the print is pressed flat.</summary>
    public const double MinLean = 0.8;

    public const double MaxLean = 1.6;

    /// <summary>A new print's resting tilt: <see cref="BoardMetrics.MinTilt"/> to <see cref="BoardMetrics.MaxTilt"/>, either way.</summary>
    public static double Next(Random random)
    {
        var side = random.NextDouble() < 0.5 ? -1 : 1;
        return side * Between(random, BoardMetrics.MinTilt, BoardMetrics.MaxTilt);
    }

    /// <summary>The lean for a pin-on of a print tilted <paramref name="tilt"/>: its way (right for none), 0.8 to 1.6.</summary>
    public static double Lean(double tilt, Random random) => Side(tilt) * Between(random, MinLean, MaxLean);

    /// <summary>Which way a print tilts: -1 or 1, and 1 for none. Swings and falls go by it.</summary>
    public static int Side(double tilt) => tilt < 0 ? -1 : 1;

    static double Between(Random random, double low, double high) => low + random.NextDouble() * (high - low);
}
