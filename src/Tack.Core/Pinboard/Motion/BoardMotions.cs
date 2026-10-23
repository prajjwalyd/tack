namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// Every motion of a print as data, at the clock's speed: the pin-on, the
/// keep press, the brass glint, the copied badge, the refresh flashes and
/// their reduced variants. Each names the layer it moves. The fall and the
/// quiet removal are <see cref="LeaveMotions"/>, the tip slip
/// <see cref="TipMotions"/>, the flight <see cref="FlightPath"/>, the swing
/// <see cref="Swing"/>. The tests hold every one of their times to its class
/// (MotionPolicy in Tack.Core.Tests). Durations at Slow are in ms.
/// </summary>
public static class BoardMotions
{
    /// <summary>Into a pin-on, when the pin starts to drive in.</summary>
    public const double PinAtMs = 250;

    /// <summary>Into the pin's drop, when the tock sounds.</summary>
    public const double TockAfterMs = 95;

    /// <summary>Into the pin's drop, when the print has arrived and swings.</summary>
    public const double ArrivedAfterMs = 150;

    /// <summary>Under reduced motion, when the tock sounds.</summary>
    public const double ReducedTockMs = 140;

    /// <summary>The pin's press when kept; the row's new order waits for it.</summary>
    public const double KeepPressMs = 360;

    /// <summary>Of the keep press, how much passes before the kept look flips.</summary>
    public const double KeepFlip = 0.3;

    /// <summary>How long the copied check stays, and the least it stays at any speed.</summary>
    public const double BadgeHoldMs = 1000;

    public const double BadgeFloorMs = 600;

    /// <summary>The settle after a pin-on, against the tilt; a copy's nudge, with it.</summary>
    public const double ArrivalSwing = 0.6;

    public const double CopySwing = 1.2;

    /// <summary>Held just above the cork, then pressed down (cubic .45, 0, .25, 1).</summary>
    public static CubicBezier Press { get; } = new(0.45, 0, 0.25, 1);

    /// <summary>Pin-on, the drop layer: held just above the cork, turned by <paramref name="lean"/> degrees, then pressed down.</summary>
    public static Timeline PinOnDrop(MotionClock clock, double lean) => new Timeline(clock.Scaled(330))
        .Key(0, new Pose { Opacity = 0, Y = -7, Scale = 1.06, Rotation = lean })
        .Key(0.2, new Pose { Opacity = 1, Y = -6, Scale = 1.06, Rotation = lean }, Press)
        .Key(1, Pose.None with { Opacity = 1 });

    /// <summary>Pin-on, the lift shadow: wide and soft while held, gone as the print meets the cork.</summary>
    public static Timeline PinOnShade(MotionClock clock) => new Timeline(clock.Scaled(330))
        .Underlying(Channel.Opacity, 0)
        .Key(0, new Pose { Opacity = 1, Y = 4, Scale = 1.03 })
        .Key(0.2, new Pose { Opacity = 1, Y = 4, Scale = 1.03 }, Press)
        .Key(1, Pose.None with { Opacity = 0 });

    /// <summary>The pin layer driving in after <paramref name="delayMs"/> (already scaled), showing its first frame meanwhile.</summary>
    public static Timeline PinDrop(MotionClock clock, double delayMs) =>
        new Timeline(clock.Scaled(170), delayMs, new CubicBezier(0.5, 0, 0.9, 0.4), Fill.Backwards)
            .Key(0, new Pose { Opacity = 0, Y = -4, Scale = 1.7 })
            .Key(0.6, new Pose { Opacity = 1, Scale = 0.9 }, new CubicBezier(0.3, 1.4, 0.5, 1))
            .Key(1, Pose.None with { Opacity = 1 });

    /// <summary>Pin-on, the contact shadow spreading as the pin goes in, back to its resting <paramref name="rest"/> opacity.</summary>
    public static Timeline Dimple(MotionClock clock, double rest)
    {
        var ease = new CubicBezier(0.2, 0.7, 0.3, 1);
        return new Timeline(clock.Scaled(300), clock.Scaled(330), fill: Fill.Backwards)
            .Underlying(Channel.Opacity, rest)
            .Key(0, new Pose { Opacity = 0.3, Scale = 0.55 }, ease)
            .Key(0.45, new Pose { Opacity = 1, Scale = 1.12 }, ease)
            .Key(1, new Pose { Opacity = rest, Scale = 1 });
    }

    /// <summary>Pin-on under reduced motion, the drop layer: a plain fade in.</summary>
    public static Timeline ReducedArrival(MotionClock clock) => Fade(clock.Scaled(180), 0, 1, CubicBezier.EaseOut);

    /// <summary>Pin-on under reduced motion, the pin layer: a fade in a moment later.</summary>
    public static Timeline ReducedPin(MotionClock clock) =>
        new Timeline(clock.Scaled(160), clock.Scaled(120), fill: Fill.Backwards)
            .Key(0, new Pose { Opacity = 0 })
            .Key(1, new Pose { Opacity = 1 });

    /// <summary>The keep press, the pin layer: pressed in, springing back a little proud.</summary>
    public static Timeline KeepPin(MotionClock clock) =>
        new Timeline(clock.Scaled(KeepPressMs), easing: new CubicBezier(0.4, 0, 0.6, 1))
            .Key(0, Pose.None)
            .Key(0.32, new Pose { Scale = 0.7 }, new CubicBezier(0.2, 0.9, 0.3, 1.5))
            .Key(0.7, new Pose { Scale = 1.1 })
            .Key(1, Pose.None);

    /// <summary>The keep press, the drop layer: the print gives a little under the pin.</summary>
    public static Timeline KeepDrop(MotionClock clock) =>
        new Timeline(clock.Scaled(KeepPressMs) - clock.Scaled(40), easing: CubicBezier.EaseOut)
            .Key(0, Pose.None)
            .Key(0.32, new Pose { Scale = 0.985 })
            .Key(1, Pose.None);

    /// <summary>The glint layer: a band of light crossing a kept pin's brass, from <paramref name="travel"/> px left of it to as far right.</summary>
    public static Timeline Glint(MotionClock clock, double travel) =>
        new Timeline(clock.Scaled(520), clock.Scaled(260), fill: Fill.Backwards)
            .Underlying(Channel.Opacity, 0)
            .Underlying(Channel.X, -travel)
            .Key(0, new Pose { Opacity = 1, X = -travel }, new CubicBezier(0.4, 0, 0.3, 1))
            .Key(1, new Pose { Opacity = 1, X = travel });

    /// <summary>The badge layer: the copied check stamping on; at once under reduced motion.</summary>
    public static Timeline BadgeIn(MotionClock clock, bool reduced) =>
        new Timeline(reduced ? 1 : clock.Scaled(230), easing: new CubicBezier(0.3, 0.8, 0.4, 1), fill: Fill.Forwards)
            .Key(0, new Pose { Opacity = 0, Scale = 1.4 })
            .Key(0.6, new Pose { Opacity = 1, Scale = 0.95 })
            .Key(1, Pose.None with { Opacity = 1 });

    /// <summary>The badge layer: the check going away after its hold.</summary>
    public static Timeline BadgeOut(MotionClock clock) =>
        new Timeline(clock.Scaled(200), easing: CubicBezier.EaseIn, fill: Fill.Forwards)
            .Key(0, Pose.None with { Opacity = 1 })
            .Key(1, new Pose { Opacity = 0, Scale = 0.92 });

    /// <summary>How long the copied check stays before it goes (class B).</summary>
    public static double BadgeHold(MotionClock clock) => clock.ScaledMin(BadgeHoldMs, BadgeFloorMs);

    /// <summary>A photo's new thumbnail flashing in, on the picture.</summary>
    public static Timeline PictureFlash(MotionClock clock) => Fade(clock.Scaled(360), 0.3, 1, CubicBezier.EaseOut);

    /// <summary>A note's new text flashing in, on the paper.</summary>
    public static Timeline PaperFlash(MotionClock clock) => Fade(clock.Scaled(360), 0.4, 1, CubicBezier.EaseOut);

    /// <summary>Opacity from <paramref name="from"/> to <paramref name="to"/> over <paramref name="ms"/> (already scaled).</summary>
    public static Timeline Fade(double ms, double from, double to, Easing? easing = null, Fill fill = Fill.None) =>
        new Timeline(ms, easing: easing, fill: fill)
            .Key(0, new Pose { Opacity = from })
            .Key(1, new Pose { Opacity = to });
}
