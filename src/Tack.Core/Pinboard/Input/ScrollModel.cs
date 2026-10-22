using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Pinboard.Input;

/// <summary>
/// Scrolling the row, as numbers: where it shows, where it heads, and how far
/// it can go. It approaches its target exponentially, a step per frame, so
/// quick notches gather into one run; a wheel's notch, a touchpad's fine step
/// and the board's own glides each catch up at their own pace. Positions are
/// logical px from the row's start, 0 to <see cref="Max"/>. The caller runs
/// <see cref="Step"/> on each frame while <see cref="Moving"/>, and only then.
/// </summary>
public sealed class ScrollModel(MotionClock clock)
{
    /// <summary>How quickly a wheel's glide catches up, ms at Slow. Class A.</summary>
    public const double WheelTauMs = 95;

    /// <summary>A touchpad's smoothing, ms. Class C: a filter on the touchpad's own momentum, not an animation.</summary>
    public const double TouchpadTauMs = 22;

    /// <summary>The board's own glides (to show a print, back from a shorter row's end), ms at Slow. Class A.</summary>
    public const double GlideTauMs = 110;

    /// <summary>Under reduced motion every step is all but there at the next frame, ms.</summary>
    public const double ReducedTauMs = 1;

    /// <summary>A longer gap between frames counts as this, ms, so a stalled frame never jumps.</summary>
    public const double MaxFrameMs = 48;

    /// <summary>Closer than this to the target, px, it snaps there and stops.</summary>
    public const double SettlePx = 0.25;

    /// <summary>Within this of an end, px, that side has nothing more to show.</summary>
    const double EdgePx = 0.5;

    /// <summary>Where the row shows now.</summary>
    public double Position { get; private set; }

    /// <summary>Where it heads.</summary>
    public double Target { get; private set; }

    /// <summary>How far it can scroll; 0 when the row fits.</summary>
    public double Max { get; private set; }

    /// <summary>The current pace of the approach, ms.</summary>
    public double TauMs { get; private set; } = clock.Scaled(WheelTauMs);

    /// <summary>Heading somewhere: the caller steps it each frame.</summary>
    public bool Moving { get; private set; }

    /// <summary>The row is wider than the board.</summary>
    public bool Overflows => Max > 0;

    /// <summary>The sides with more prints out of sight.</summary>
    public ScrollEdges Edges => Overflows ? new ScrollEdges(Position > EdgePx, Position < Max - EdgePx) : default;

    /// <summary>A position kept within 0 to <see cref="Max"/>.</summary>
    public double Clamp(double x) => Math.Max(0, Math.Min(Max, x));

    /// <summary>
    /// The row's new extent. Scrolled past a shorter row's end, it glides back
    /// to the new end if <paramref name="glide"/> (the board is down and motion
    /// not reduced), else it jumps there.
    /// </summary>
    public void SetExtent(double max, bool glide)
    {
        Max = Math.Max(0, max);
        if (Target <= Max && Position <= Max)
        {
            return;
        }
        Target = Math.Min(Target, Max);
        if (glide)
        {
            TauMs = clock.Scaled(GlideTauMs);
            Moving = true;
            return;
        }
        Moving = false;
        Position = Math.Min(Position, Max);
    }

    /// <summary>Heads for <paramref name="x"/> (kept within the row), gliding, or there at once if <paramref name="instant"/>.</summary>
    public void ScrollTo(double x, bool instant)
    {
        Target = Clamp(x);
        if (instant)
        {
            Moving = false;
            Position = Target;
            return;
        }
        TauMs = clock.Scaled(GlideTauMs);
        Moving = true;
    }

    /// <summary>
    /// A wheel or touchpad step. A notch while gliding stacks onto the target,
    /// not the spot shown. False, and nothing moves, if the board is not
    /// <paramref name="revealed"/> or the row fits.
    /// </summary>
    public bool Wheel(WheelTurn turn, bool revealed, bool reduced)
    {
        if (!revealed || !Overflows)
        {
            return false;
        }
        if (turn.Delta == 0)
        {
            return true;
        }
        TauMs = reduced ? ReducedTauMs
            : turn.Notch ? clock.Scaled(WheelTauMs)
            : MotionClock.Fixed(TouchpadTauMs, "a touchpad's smoothing filter, not an animation");
        Target = Clamp(Target + turn.Delta);
        Moving = true;
        return true;
    }

    /// <summary>Stops dead where it shows (the tuck).</summary>
    public void Halt()
    {
        Moving = false;
        Target = Position;
    }

    /// <summary>One frame, <paramref name="dtMs"/> after the last (or after it started moving).</summary>
    public void Step(double dtMs)
    {
        if (!Moving)
        {
            return;
        }
        var k = 1 - Math.Exp(-Math.Min(MaxFrameMs, dtMs) / TauMs);
        Position += (Target - Position) * k;
        if (Math.Abs(Target - Position) < SettlePx)
        {
            Position = Target;
        }
        Moving = Position != Target;
    }
}
