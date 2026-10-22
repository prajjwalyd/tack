namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A print leaving the board, as data at the clock's speed: the fall (the pin
/// pops out and the print tumbles into the overhang), the quiet fade, and the
/// fall under reduced motion. Each holds its last frame until the print is gone.
/// </summary>
public static class LeaveMotions
{
    /// <summary>After the pin pops, when the print starts to fall.</summary>
    public const double FallDelayMs = 50;

    /// <summary>The fall's spin, degrees, either way: from this to <see cref="MaxSpin"/>.</summary>
    public const double MinSpin = 6;

    public const double MaxSpin = 10;

    /// <summary>The fall, the pin layer: it pops up and out, toward <paramref name="direction"/> (1 right, -1 left), and fades.</summary>
    public static Timeline FallPin(MotionClock clock, int direction) =>
        new Timeline(clock.Scaled(200), easing: new CubicBezier(0.2, 0.7, 0.4, 1), fill: Fill.Forwards)
            .Key(0, Pose.None with { Opacity = 1 })
            .Key(0.4, new Pose { Opacity = 1, X = direction, Y = -5, Scale = 1.12 })
            .Key(1, new Pose { Opacity = 0, X = 3 * direction, Y = -9, Scale = 1 });

    /// <summary>The fall, the drop layer: a little hop, then down into the overhang turning <paramref name="spin"/> degrees toward <paramref name="direction"/>.</summary>
    public static Timeline FallDrop(MotionClock clock, int direction, double spin) =>
        new Timeline(clock.Scaled(450), clock.Scaled(FallDelayMs), fill: Fill.Forwards)
            .Key(0, Pose.None with { Opacity = 1 })
            .Key(0.12, new Pose { Opacity = 1, Y = -2, Rotation = 0.6 * direction }, new CubicBezier(0.5, 0, 0.9, 0.55))
            .Key(0.5, new Pose { Opacity = 0.85 })
            .Key(1, new Pose { Opacity = 0, X = 6 * direction, Y = 64, Rotation = direction * spin });

    /// <summary>The quiet removal, the drop layer: a quick fade as it shrinks a touch.</summary>
    public static Timeline Quiet(MotionClock clock) =>
        new Timeline(clock.Scaled(170), easing: CubicBezier.EaseIn, fill: Fill.Forwards)
            .Key(0, Pose.None with { Opacity = 1 })
            .Key(1, new Pose { Opacity = 0, Scale = 0.96 });

    /// <summary>The fall under reduced motion, the drop layer: a short drop as it fades.</summary>
    public static Timeline ReducedFall(MotionClock clock) =>
        new Timeline(clock.Scaled(220), easing: CubicBezier.EaseIn, fill: Fill.Forwards)
            .Key(0, Pose.None with { Opacity = 1 })
            .Key(1, new Pose { Opacity = 0, Y = 10 });

    /// <summary>The pin layer fading out, for a quiet removal and a reduced fall.</summary>
    public static Timeline PinFade(MotionClock clock) => BoardMotions.Fade(clock.Scaled(140), 1, 0, fill: Fill.Forwards);

    /// <summary>The longest any leaving motion runs, delay included: when the print may be forgotten.</summary>
    public static double LongestMs(MotionClock clock) => clock.Scaled(FallDelayMs + 450);
}
