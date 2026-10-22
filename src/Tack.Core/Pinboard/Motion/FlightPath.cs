using Tack.Core.Geometry;

namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A new capture's flight onto the board, as frames for a stand-in laid out
/// where the print will rest and turned and scaled about its pin point. It
/// lifts off where it was taken (the photo, inside the paper border, exactly
/// over the snip), arcs to its slot along a quadratic curve, shrinking a
/// little ahead of the travel and turning to its tilt plus a lean, is held
/// just above the cork, and is pressed down to rest. A capture not found on
/// screen grows out of the pointer instead. The stand-in shows until half a
/// millisecond before the end, where the print takes over. Window logical px.
/// </summary>
public sealed class FlightPath
{
    /// <summary>Frames over the flight, played straight between them.</summary>
    public const int FrameCount = 72;

    /// <summary>A wait on the picture's decoding before the thumbnail stands in for it. Class C.</summary>
    public const double DecodeWaitMs = 120;

    const double LiftSwell = 1.025;
    const double HeldY = -6;
    const double HeldScale = 1.06;
    const double ShrinkAhead = 1.08;

    static readonly CubicBezier TravelEasing = new(0.5, 0, 0.25, 1);

    readonly MotionClock _clock;
    readonly FlightFrame[] _frames;

    /// <param name="from">Where the capture was taken (or, not found, a small box at the pointer).</param>
    /// <param name="rest">The print's slot at rest: its size, and where the stand-in is laid out.</param>
    /// <param name="tilt">The print's resting tilt, degrees.</param>
    /// <param name="lean">The extra turn it is held at before it is pressed down, degrees.</param>
    /// <param name="found">The capture was found on screen, so <paramref name="from"/> is exactly where it was.</param>
    /// <param name="windowWidth">The window's width: a flight from straight below bows away from its middle.</param>
    /// <param name="clock">The speed it flies at.</param>
    public FlightPath(LogicalRect from, LogicalRect rest, double tilt, double lean, bool found, double windowWidth, MotionClock clock)
    {
        _clock = clock;
        Times = FlightTimes.At(clock);
        var frames = Trace(from, rest, tilt, lean, found, windowWidth, Times);
        // Hidden on the frame the print takes over; the last frame is its resting pose.
        var last = frames[^1];
        frames.Insert(frames.Count - 1, last with { Offset = 1 - 0.5 / Times.Total, Opacity = 1 });
        frames[^1] = last with { Opacity = 0 };
        _frames = [.. frames];
    }

    public FlightTimes Times { get; }

    /// <summary>The stand-in's frames: <see cref="FrameCount"/> + 1 evenly spaced, and the hand-over just before the end.</summary>
    public IReadOnlyList<FlightFrame> Frames => _frames;

    /// <summary>The stand-in, straight between its frames, holding its last (hidden) one.</summary>
    public Timeline StandIn()
    {
        var timeline = new Timeline(Times.Total, fill: Fill.Forwards);
        foreach (var f in _frames)
        {
            timeline.Key(f.Offset, new Pose { Opacity = f.Opacity, X = f.X, Y = f.Y, Rotation = f.Rotation, ScaleX = f.ScaleX, ScaleY = f.ScaleY });
        }
        return timeline;
    }

    /// <summary>The stand-in's lift shadow: growing as it lifts, strongest as it is pressed, gone at the end.</summary>
    public Timeline Shade() => new Timeline(Times.Total, fill: Fill.Forwards)
        .Key(0, new Pose { Opacity = 0 })
        .Key(At(Times.Lift), new Pose { Opacity = 0.75 }, CubicBezier.EaseInOut)
        .Key(At(Times.PressAt), new Pose { Opacity = 1 }, new CubicBezier(0.5, 0, 0.3, 1))
        .Key(1, new Pose { Opacity = 0 });

    /// <summary>The stand-in's paper: the bare picture gains its border on the way.</summary>
    public Timeline Paper() => new Timeline(Times.Total, fill: Fill.Forwards)
        .Key(0, new Pose { Opacity = 0 })
        .Key(At(Times.Lift + Times.Travel * 0.12), new Pose { Opacity = 0 }, CubicBezier.EaseInOut)
        .Key(At(Times.Lift + Times.Travel * 0.6), new Pose { Opacity = 1 })
        .Key(1, new Pose { Opacity = 1 });

    /// <summary>The pin driving in at the touch, on the stand-in's pin and on the print's own.</summary>
    public Timeline Pin() => BoardMotions.PinDrop(_clock, Times.PinAt);

    /// <summary>The print's drop layer, unseen for the whole flight while the stand-in carries it.</summary>
    public Timeline PrintHold() => new Timeline(Times.Total).Key(0, new Pose { Opacity = 0 }).Key(1, new Pose { Opacity = 0 });

    double At(double ms) => ms / Times.Total;

    static List<FlightFrame> Trace(
        LogicalRect from, LogicalRect rest, double tilt, double lean, bool found, double windowWidth, FlightTimes t)
    {
        var border = BoardMetrics.PrintBorder;
        var (ox, oy) = (rest.X + rest.W / 2, rest.Y + BoardMetrics.PinY);
        var (pw, ph) = (Math.Max(1, rest.W - 2 * border), Math.Max(1, rest.H - 2 * border));
        // The photo's centre below the pin, unscaled.
        var d = rest.H / 2 - BoardMetrics.PinY;
        var (sx0, sy0) = (from.W / pw, from.H / ph);
        var r1 = tilt + lean;
        var c0 = (X: from.X + from.W / 2, Y: from.Y + from.H / 2);
        var end = Turn(r1, HeldScale * d);
        var c1 = (X: ox + end.X, Y: oy + HeldY + end.Y);
        var p1 = Control(c0, c1, windowWidth);
        var restC = Turn(tilt, d);
        var c2 = (X: ox + restC.X, Y: oy + restC.Y);
        var frames = new List<FlightFrame>(FrameCount + 2);
        for (var i = 0; i <= FrameCount; i++)
        {
            var ms = i / (double)FrameCount * t.Total;
            (double X, double Y) c;
            double sx, sy, r, opacity = 1;
            if (ms > t.PressAt)
            {
                var k = BoardMotions.Press.At((ms - t.PressAt) / t.Press);
                c = (c1.X + (c2.X - c1.X) * k, c1.Y + (c2.Y - c1.Y) * k);
                sx = sy = HeldScale + (1 - HeldScale) * k;
                r = r1 + (tilt - r1) * k;
            }
            else if (ms <= t.Lift)
            {
                var swell = 1 + (LiftSwell - 1) * EaseOut(ms / t.Lift);
                (c, sx, sy, r) = (c0, sx0 * swell, sy0 * swell, 0);
            }
            else
            {
                var u = TravelEasing.At((ms - t.Lift) / t.Travel);
                c = Bezier(c0, p1, c1, u);
                var us = Math.Min(1, u * ShrinkAhead);
                sx = Geometric(sx0 * LiftSwell, HeldScale, us);
                sy = Geometric(sy0 * LiftSwell, HeldScale, us);
                r = r1 * Smooth(0.25, 1, u);
            }
            if (!found)
            {
                var k = EaseOut(Math.Min(1, ms / t.Grow));
                opacity = k;
                var grow = 0.55 + 0.45 * k;
                (sx, sy) = (sx * grow, sy * grow);
            }
            var v = Turn(r, sy * d);
            frames.Add(new FlightFrame(i / (double)FrameCount, c.X - ox - v.X, c.Y - oy - v.Y, r, sx, sy, opacity));
        }
        return frames;
    }

    /// <summary>The arc's control point: most of the way across early, so the print rises into its slot from below; bowing sideways when nearly straight below.</summary>
    static (double X, double Y) Control((double X, double Y) a, (double X, double Y) b, double windowWidth)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var p = (X: a.X + dx * 0.7, Y: a.Y + dy * 0.2);
        var length = Math.Sqrt(dx * dx + dy * dy);
        length = length == 0 ? 1 : length;
        var bow = Math.Max(0, 0.14 - Math.Abs(dx) / length * 0.3) * length;
        if (bow > 0)
        {
            p.X += (a.X < windowWidth / 2 ? -1 : 1) * bow;
        }
        return p;
    }

    static (double X, double Y) Bezier((double X, double Y) a, (double X, double Y) p, (double X, double Y) b, double u)
    {
        var v = 1 - u;
        return (v * v * a.X + 2 * v * u * p.X + u * u * b.X, v * v * a.Y + 2 * v * u * p.Y + u * u * b.Y);
    }

    /// <summary>The point (0, <paramref name="y"/>) turned <paramref name="degrees"/> about the origin.</summary>
    static (double X, double Y) Turn(double degrees, double y)
    {
        var a = degrees * Math.PI / 180;
        return (-y * Math.Sin(a), y * Math.Cos(a));
    }

    static double Geometric(double a, double b, double u) => a * Math.Pow(b / a, u);

    static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    static double Smooth(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
