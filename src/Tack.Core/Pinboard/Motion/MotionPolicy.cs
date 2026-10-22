namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// Every duration, delay and timer of the print motions, each with its class
/// and worked out by the motion's own code, so a test can hold each to its
/// class: the Speed setting must move the whole board, uniformly, and what
/// must not follow it says why.
/// </summary>
public static class MotionPolicy
{
    public static IReadOnlyList<MotionTiming> All { get; } =
    [
        A("pin-on drop", 330, c => BoardMotions.PinOnDrop(c, 1).Duration),
        A("pin-on shade", 330, c => BoardMotions.PinOnShade(c).Duration),
        A("pin-on pin delay", BoardMotions.PinAtMs, c => c.Scaled(BoardMotions.PinAtMs)),
        A("pin drop", 170, c => BoardMotions.PinDrop(c, 0).Duration),
        A("pin-on tock", 345, c => c.Scaled(BoardMotions.PinAtMs) + c.Scaled(BoardMotions.TockAfterMs)),
        A("pin-on arrived", 400, c => c.Scaled(BoardMotions.PinAtMs) + c.Scaled(BoardMotions.ArrivedAfterMs)),
        A("dimple", 300, c => BoardMotions.Dimple(c, 0.9).Duration),
        A("dimple delay", 330, c => BoardMotions.Dimple(c, 0.9).Delay),
        A("reduced arrival", 180, c => BoardMotions.ReducedArrival(c).Duration),
        A("reduced pin", 160, c => BoardMotions.ReducedPin(c).Duration),
        A("reduced pin delay", 120, c => BoardMotions.ReducedPin(c).Delay),
        A("reduced tock", BoardMotions.ReducedTockMs, c => c.Scaled(BoardMotions.ReducedTockMs)),
        A("keep press", BoardMotions.KeepPressMs, c => BoardMotions.KeepPin(c).Duration),
        A("keep give", 320, c => BoardMotions.KeepDrop(c).Duration),
        A("keep flip", 108, c => BoardMotions.KeepPin(c).Duration * BoardMotions.KeepFlip),
        A("glint", 520, c => BoardMotions.Glint(c, 10).Duration),
        A("glint delay", 260, c => BoardMotions.Glint(c, 10).Delay),
        A("badge in", 230, c => BoardMotions.BadgeIn(c, reduced: false).Duration),
        new("badge hold", MotionClass.B, BoardMotions.BadgeHoldMs, BoardMotions.BadgeHold, Floor: BoardMotions.BadgeFloorMs),
        A("badge out", 200, c => BoardMotions.BadgeOut(c).Duration),
        A("picture flash", 360, c => BoardMotions.PictureFlash(c).Duration),
        A("paper flash", 360, c => BoardMotions.PaperFlash(c).Duration),
        A("fall pin", 200, c => LeaveMotions.FallPin(c, 1).Duration),
        A("fall", 450, c => LeaveMotions.FallDrop(c, 1, 8).Duration),
        A("fall delay", LeaveMotions.FallDelayMs, c => LeaveMotions.FallDrop(c, 1, 8).Delay),
        A("quiet removal", 170, c => LeaveMotions.Quiet(c).Duration),
        A("reduced fall", 220, c => LeaveMotions.ReducedFall(c).Duration),
        A("pin fade", 140, c => LeaveMotions.PinFade(c).Duration),
        A("swing", Swing.Nudge(BoardMotions.ArrivalSwing, 1).WallMs, c => Swing.Nudge(BoardMotions.ArrivalSwing, c.Scale).WallMs),
        A("breeze ripple", 165, c => Breeze.Plan(4, c, () => 0).Max(p => p.DelayMs)),
        new("breeze interval", MotionClass.C, Breeze.MinIntervalMs, _ => Breeze.NextMs(() => 0),
            Why: "the breeze is ambient: its cadence is not a response to anything"),
        A("flight", 890, c => FlightTimes.At(c).Total),
        A("flight pin", 790, c => FlightTimes.At(c).PinAt),
        A("flight tock", 885, c => FlightTimes.At(c).TockAt),
        A("flight hand-over", 950, c => FlightTimes.At(c).SettledAt),
        A("flight grow", 220, c => FlightTimes.At(c).Grow),
        new("flight decode wait", MotionClass.C, FlightPath.DecodeWaitMs, _ => MotionClock.Fixed(FlightPath.DecodeWaitMs, "a wait on the picture's decoding"),
            Why: "a wait on the picture's decoding, not choreography"),
        A("tip delay", TipMotions.DelayMs, c => FlightTimes.At(c).TipDelay),
        A("reduced tip delay", TipMotions.ReducedDelayMs, c => c.Scaled(TipMotions.ReducedDelayMs)),
        A("tip in", 520, c => TipMotions.In(c).Duration),
        A("reduced tip in", 200, c => TipMotions.ReducedIn(c).Duration),
        A("tip out", 200, c => TipMotions.Out(c, reduced: false).Duration),
        A("reduced tip out", 120, c => TipMotions.Out(c, reduced: true).Duration),
    ];

    /// <summary>What <paramref name="timing"/> must be at the speed of <paramref name="clock"/>, by its class.</summary>
    public static double Expected(MotionTiming timing, MotionClock clock) => timing.Class switch
    {
        MotionClass.A => clock.Scaled(timing.SlowMs),
        MotionClass.B => clock.ScaledMin(timing.SlowMs, timing.Floor ?? 0),
        _ => timing.SlowMs,
    };

    static MotionTiming A(string name, double slowMs, Func<MotionClock, double> at) => new(name, MotionClass.A, slowMs, at);
}
