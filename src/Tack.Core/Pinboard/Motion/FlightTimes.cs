namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A flight's phases and moments at the clock's speed, ms (class A: a faster
/// flight keeps its shape): it lifts off, travels to its slot, and is pressed
/// onto the cork as the pin drives in.
/// </summary>
public sealed record FlightTimes(
    double Lift, double Travel, double Press, double Pin, double Touch, double Sound, double Grow, double HandOver, double TipDelay)
{
    /// <summary>The whole flight, until the print rests.</summary>
    public double Total => Lift + Travel + Press;

    /// <summary>When the travel ends and the press starts.</summary>
    public double PressAt => Lift + Travel;

    /// <summary>When the pin starts to drive in: the print takes the arriving place.</summary>
    public double PinAt => Total - Touch;

    /// <summary>When the tock sounds.</summary>
    public double TockAt => PinAt + Sound;

    /// <summary>When the stand-in goes and the print, now in charge, swings.</summary>
    public double SettledAt => Total + HandOver;

    /// <summary>The times at the speed of <paramref name="clock"/>.</summary>
    public static FlightTimes At(MotionClock clock) => new(
        clock.Scaled(110), clock.Scaled(560), clock.Scaled(220), clock.Scaled(170), clock.Scaled(100), clock.Scaled(95),
        clock.Scaled(220), clock.Scaled(60), clock.Scaled(TipMotions.DelayMs));
}
