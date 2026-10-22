namespace Tack.Core.Reveal;

/// <summary>
/// What a <see cref="RevealSpeed"/> means in time. Every timer in the app is
/// one of these classes, and Slow is always the original value:
/// <list type="bullet">
/// <item>A, animation: scales with <see cref="MotionScale"/> (<see cref="Scaled"/>).</item>
/// <item>B, a hold, peek or wait after an event: scales with a floor (<see cref="ScaledMin"/>).</item>
/// <item>C, never scales: an input threshold, a physical gesture's time, or a delay that exists for correctness.</item>
/// <item>D, reading time: text keeps the time it needs (<see cref="NoticePeek"/>; the one-time tip is a fixed 9 s).</item>
/// <item>E, accessibility: reduced motion is the board's own job.</item>
/// </list>
/// </summary>
public static class RevealSpeedTimes
{
    /// <summary>The board's tuck slide at Slow, ms.</summary>
    public const int TuckMs = 190;

    /// <summary>Time after the tuck slide for its last frame to be drawn before the window hides. Class C.</summary>
    public static readonly TimeSpan TuckMargin = TimeSpan.FromMilliseconds(110);

    static readonly TimeSpan PeekTime = TimeSpan.FromMilliseconds(3000);
    static readonly TimeSpan PeekFloor = TimeSpan.FromMilliseconds(1200);
    static readonly TimeSpan FlightPeekTime = TimeSpan.FromMilliseconds(1750);
    static readonly TimeSpan FlightPeekFloor = TimeSpan.FromMilliseconds(800);
    static readonly TimeSpan NoticePeekTime = TimeSpan.FromMilliseconds(1600);

    /// <summary>The reveal swing at Slow: what a faster speed saves a notice.</summary>
    static readonly TimeSpan NoticeSwing = TimeSpan.FromMilliseconds(600);

    static readonly TimeSpan LeaveDelayTime = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan LeaveDelayFloor = TimeSpan.FromMilliseconds(120);

    /// <summary>How long the pointer must rest on the top edge before the board reveals.</summary>
    public static TimeSpan Dwell(this RevealSpeed speed) => TimeSpan.FromMilliseconds(speed switch
    {
        RevealSpeed.Instant => 40,
        RevealSpeed.Slow => 400,
        _ => 150,
    });

    /// <summary>What every animation's duration is multiplied by; 1 is the original pace.</summary>
    public static double MotionScale(this RevealSpeed speed) => speed switch
    {
        RevealSpeed.Instant => 0.35,
        RevealSpeed.Slow => 1.0,
        _ => 0.65,
    };

    /// <summary>
    /// <paramref name="d"/> at this speed (class A, no floor), to whole
    /// milliseconds and never below 1 ms. Slow is <paramref name="d"/> itself.
    /// Halves round away from zero, as Tack 0.2's did.
    /// </summary>
    public static TimeSpan Scaled(this RevealSpeed speed, TimeSpan d)
    {
        var scale = speed.MotionScale();
        if (scale == 1.0)
        {
            return d;
        }
        var ms = Math.Max(Math.Round(d.TotalSeconds * 1000.0 * scale, MidpointRounding.AwayFromZero), 1.0);
        return TimeSpan.FromMilliseconds((long)ms);
    }

    /// <summary>
    /// <paramref name="d"/> at this speed but not below <paramref name="floor"/>
    /// (class B), and never above <paramref name="d"/> itself.
    /// </summary>
    public static TimeSpan ScaledMin(this RevealSpeed speed, TimeSpan d, TimeSpan floor)
    {
        var scaled = speed.Scaled(d);
        var floored = scaled > floor ? scaled : floor;
        return floored < d ? floored : d;
    }

    /// <summary>How long a new screenshot keeps the board down on its own. Class B.</summary>
    public static TimeSpan Peek(this RevealSpeed speed) => speed.ScaledMin(PeekTime, PeekFloor);

    /// <summary>The same for a capture that flies in: its landing must stay in view. Class B.</summary>
    public static TimeSpan FlightPeek(this RevealSpeed speed) => speed.ScaledMin(FlightPeekTime, FlightPeekFloor);

    /// <summary>How long the board stays down for a notice alone. Class D: only what the quicker swing saves comes off.</summary>
    public static TimeSpan NoticePeek(this RevealSpeed speed) => NoticePeekTime - (NoticeSwing - speed.Scaled(NoticeSwing));

    /// <summary>The grace once the pointer is outside the board's zone, so a brief overshoot does not tuck it. Class B.</summary>
    public static TimeSpan LeaveDelay(this RevealSpeed speed) => speed.ScaledMin(LeaveDelayTime, LeaveDelayFloor);

    /// <summary>How long after the tuck starts the window may hide: the slide plus <see cref="TuckMargin"/>.</summary>
    public static TimeSpan TuckHideDelay(this RevealSpeed speed) =>
        speed.Scaled(TimeSpan.FromMilliseconds(TuckMs)) + TuckMargin;

    /// <summary>"instant", "normal" or "slow" exactly; anything else is null.</summary>
    public static RevealSpeed? Parse(string text) => text switch
    {
        "instant" => RevealSpeed.Instant,
        "normal" => RevealSpeed.Normal,
        "slow" => RevealSpeed.Slow,
        _ => null,
    };

    /// <summary>The saved text.</summary>
    public static string Id(this RevealSpeed speed) => speed switch
    {
        RevealSpeed.Instant => "instant",
        RevealSpeed.Slow => "slow",
        _ => "normal",
    };
}
