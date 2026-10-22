namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// The one-time tip, a paper slip pinned under the board after the first
/// flight, as data at the clock's speed: it swings in on its pin and settles
/// a little crooked, and lifts away when closed. Under reduced motion it only fades.
/// </summary>
public static class TipMotions
{
    /// <summary>The slip's resting turn, degrees; straight under reduced motion.</summary>
    public const double RestAngle = -0.6;

    /// <summary>After a flight lands, when the tip shows; under reduced motion, after the plain pin-on.</summary>
    public const double DelayMs = 380;

    public const double ReducedDelayMs = 500;

    /// <summary>The slip swinging in on its pin, settling at its resting turn.</summary>
    public static Timeline In(MotionClock clock) =>
        new Timeline(clock.Scaled(520), easing: new CubicBezier(0.3, 0.7, 0.4, 1))
            .Underlying(Channel.Rotation, RestAngle)
            .Key(0, new Pose { Opacity = 0, Y = -10, Rotation = -2.4 })
            .Key(0.45, new Pose { Opacity = 1, Y = 1, Rotation = 0.4 })
            .Key(0.75, new Pose { Y = 0, Rotation = -0.9 })
            .Key(1, new Pose { Opacity = 1, Rotation = RestAngle });

    /// <summary>The slip fading in under reduced motion.</summary>
    public static Timeline ReducedIn(MotionClock clock) => BoardMotions.Fade(clock.Scaled(200), 0, 1, CubicBezier.EaseOut);

    /// <summary>The slip lifting away when closed; under reduced motion a quicker plain fade.</summary>
    public static Timeline Out(MotionClock clock, bool reduced)
    {
        if (reduced)
        {
            return BoardMotions.Fade(clock.Scaled(120), 1, 0, CubicBezier.EaseIn, Fill.Forwards);
        }
        return new Timeline(clock.Scaled(200), easing: CubicBezier.EaseIn, fill: Fill.Forwards)
            .Key(0, new Pose { Opacity = 1, Rotation = RestAngle })
            .Key(1, new Pose { Opacity = 0, Y = -6, Rotation = -1.2, Scale = 0.98 });
    }
}
