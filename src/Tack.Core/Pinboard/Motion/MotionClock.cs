namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// How fast the board moves: the Speed setting's motion scale (0.35 Instant,
/// 0.65 Normal, 1 Slow), taken with every reveal and settings change. Every
/// duration, delay and timer on the board belongs to one class (see
/// <see cref="Reveal.RevealSpeedTimes"/>): A scales (<see cref="Scaled"/>),
/// B scales with a floor (<see cref="ScaledMin"/>), C and D never scale
/// (<see cref="Fixed"/>, which names why). All in ms, not rounded.
/// </summary>
public sealed class MotionClock
{
    /// <summary>The current scale, 1 for Slow.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Takes a new scale. Anything that is not a number in (0, 1] is ignored. Returns whether it changed.</summary>
    public bool SetScale(double next)
    {
        if (!(next > 0 && next <= 1) || next == Scale)
        {
            return false;
        }
        Scale = next;
        return true;
    }

    /// <summary>Class A: <paramref name="ms"/> as the current speed plays it.</summary>
    public double Scaled(double ms) => ms * Scale;

    /// <summary>Class B: as the current speed plays it, never below <paramref name="floor"/> nor above <paramref name="ms"/>.</summary>
    public double ScaledMin(double ms, double floor) => Math.Min(ms, Math.Max(ms * Scale, floor));

    /// <summary>Classes C and D: <paramref name="ms"/> as it is. <paramref name="why"/> says why it must not follow the speed.</summary>
    public static double Fixed(double ms, string why) => ms;

    /// <summary>
    /// After a reveal, until the board is at rest: the swing plus a couple of
    /// frames, or the plain slide under reduced motion. Pin-ons, held notices
    /// and the keyboard's hover look wait for it.
    /// </summary>
    public double RestMs(bool reduced) =>
        reduced ? Scaled(BoardMetrics.ReducedRestMs) : Scaled(Spring.Reveal.Ms) + BoardMetrics.RestMarginMs;

    /// <summary>A slide along the row at the current speed.</summary>
    public double LayoutMs => Scaled(Spring.Layout.Ms);

    /// <summary>After a tuck starts, when the board lets go of everything: the fold plus a margin of frames.</summary>
    public double TuckCleanupMs => Scaled(BoardMetrics.TuckMs) + BoardMetrics.TuckCleanupMarginMs;
}
