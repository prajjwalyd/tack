namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// The breeze: now and then, while the board is down, a faint push ripples
/// along the prints in view, one way or the other, each swinging a little
/// differently. The randomness is handed in, so a plan can be replayed.
/// </summary>
public static class Breeze
{
    /// <summary>Between breezes, ms. Class C: the breeze is ambient, its cadence a response to nothing.</summary>
    public const double MinIntervalMs = 12000;

    public const double MaxIntervalMs = 25000;

    /// <summary>The strongest push, degrees.</summary>
    public const double MaxDegrees = 0.8;

    /// <summary>From one print to the next along the ripple, ms at Slow, plus up to <see cref="JitterMs"/>.</summary>
    public const double RippleMs = 55;

    public const double JitterMs = 30;

    /// <summary>When the next breeze comes, ms from now; <paramref name="random"/> gives numbers from 0 to 1.</summary>
    public static double NextMs(Func<double> random) => MotionClock.Fixed(
        Between(random, MinIntervalMs, MaxIntervalMs), "the breeze is ambient: its cadence is not a response to anything");

    /// <summary>
    /// One breeze over <paramref name="count"/> prints in view, in row order:
    /// a direction, a strength, then each print's place along the ripple and
    /// its own share of the push. The pushes come in ripple order; each names
    /// its print by its index in the row.
    /// </summary>
    public static IReadOnlyList<BreezePush> Plan(int count, MotionClock clock, Func<double> random)
    {
        if (count <= 0)
        {
            return [];
        }
        var direction = random() < 0.5 ? -1 : 1;
        var strength = Between(random, 0.65, 1);
        var delays = new double[count];
        for (var i = 0; i < count; i++)
        {
            delays[i] = clock.Scaled(i * RippleMs + Between(random, 0, JitterMs));
        }
        var pushes = new BreezePush[count];
        for (var i = 0; i < count; i++)
        {
            var index = direction > 0 ? i : count - 1 - i;
            pushes[i] = new BreezePush(index, delays[i], direction * MaxDegrees * strength * Between(random, 0.75, 1));
        }
        return pushes;
    }

    static double Between(Func<double> random, double from, double to) => from + random() * (to - from);
}
