using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>
/// How a duration, delay or timer on the board follows the Speed setting
/// (the classes of <see cref="Tack.Core.Reveal.RevealSpeedTimes"/>).
/// </summary>
enum MotionClass
{
    /// <summary>An animation, or a delay inside one: scales (<see cref="MotionClock.Scaled"/>).</summary>
    A,

    /// <summary>A hold after an event: scales, with a floor (<see cref="MotionClock.ScaledMin"/>).</summary>
    B,

    /// <summary>An input threshold, a gesture's time, a wait on the system or an ambient cadence: never scales.</summary>
    C,

    /// <summary>Reading time: never scales.</summary>
    D,
}
