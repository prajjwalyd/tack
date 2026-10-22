namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// One duration, delay or timer of the board's motion, with its class: what
/// it is at Slow, and how the board works it out at a given speed.
/// </summary>
/// <param name="Name">What it times, in a few words.</param>
/// <param name="Class">How it follows the speed.</param>
/// <param name="SlowMs">What it is at Slow (motion scale 1), ms.</param>
/// <param name="At">How the board works it out at the clock's speed: the motion's own code.</param>
/// <param name="Floor">Class B: the least it may be.</param>
/// <param name="Why">Classes C and D: why it must not follow the speed.</param>
public sealed record MotionTiming(
    string Name, MotionClass Class, double SlowMs, Func<MotionClock, double> At, double? Floor = null, string? Why = null);
