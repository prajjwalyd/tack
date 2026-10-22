namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A print swinging on its pin: one damped oscillator, about a 1.3 s period,
/// dying away at 1.7 a second. A nudge pushes it to peak at about its
/// amplitude, adding to a swing already going, so nudges blend. The swing
/// runs in its own time, which the Speed setting stretches: at Instant it is
/// the same swing played quicker (class A). Its frames are taken 30 to the
/// oscillator's second, the last at rest, to be played straight between.
/// </summary>
public sealed class Swing
{
    /// <summary>The angular frequency, radians a second: paper on a pin, not a pendulum clock.</summary>
    public const double Omega = 2 * Math.PI / 1.3;

    /// <summary>How fast it dies away, a second.</summary>
    public const double Decay = 1.7;

    /// <summary>A nudge's push, as a share of its amplitude times <see cref="Omega"/>.</summary>
    const double Kick = 1.25;

    /// <summary>The swing ends when it would peak below this many degrees.</summary>
    const double Rest = 0.03;

    const double MinSeconds = 0.6;
    const double MaxSeconds = 3.5;
    const double FramesPerSecond = 30;

    readonly double[] _frames;

    Swing(double c1, double c2, double scale)
    {
        (C1, C2, Scale) = (c1, c2, scale);
        var peak = Math.Max(Math.Max(Math.Abs(c1), Math.Abs(c2)), 0.01);
        Duration = Math.Clamp(Math.Log(peak / Rest) / Decay, MinSeconds, MaxSeconds);
        var n = (int)Math.Ceiling(Duration * FramesPerSecond);
        _frames = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            _frames[i] = At(i / (double)n * Duration).Theta;
        }
    }

    /// <summary>The angle's cosine part: where it starts, degrees.</summary>
    public double C1 { get; }

    /// <summary>The angle's sine part, from the speed it starts at.</summary>
    public double C2 { get; }

    /// <summary>How long it lasts in its own time, seconds.</summary>
    public double Duration { get; }

    /// <summary>Wall seconds per second of its own time: the speed's motion scale when it was nudged.</summary>
    public double Scale { get; }

    /// <summary>How long it lasts on the wall clock, ms.</summary>
    public double WallMs => Duration * 1000 * Scale;

    /// <summary>The angle at each frame, degrees, from the start to exactly 0 at the end.</summary>
    public IReadOnlyList<double> Frames => _frames;

    /// <summary>
    /// A nudge to peak at about <paramref name="amplitude"/> degrees, at the
    /// speed's <paramref name="scale"/>, onto <paramref name="current"/> (if any)
    /// <paramref name="elapsedMs"/> wall ms into it.
    /// </summary>
    public static Swing Nudge(double amplitude, double scale, Swing? current = null, double elapsedMs = 0)
    {
        var (theta, velocity) = current?.AtWall(elapsedMs) ?? (0, 0);
        velocity += amplitude * Omega * Kick;
        return new Swing(theta, (velocity + Decay * theta) / Omega, scale);
    }

    /// <summary>The angle (degrees) and its speed (degrees a second) <paramref name="t"/> seconds into its own time; at rest once over.</summary>
    public (double Theta, double Velocity) At(double t)
    {
        if (t >= Duration)
        {
            return (0, 0);
        }
        var (e, c, s) = (Math.Exp(-Decay * t), Math.Cos(Omega * t), Math.Sin(Omega * t));
        return (
            e * (C1 * c + C2 * s),
            e * ((-Decay * C1 + Omega * C2) * c + (-Decay * C2 - Omega * C1) * s));
    }

    /// <summary>As <see cref="At"/>, <paramref name="ms"/> wall ms after the nudge.</summary>
    public (double Theta, double Velocity) AtWall(double ms) => At(ms / 1000 / Scale);

    /// <summary>The swing as a turn of the swing layer over its wall time, straight between frames.</summary>
    public Timeline Timeline()
    {
        var timeline = new Timeline(WallMs);
        var n = _frames.Length - 1;
        for (var i = 0; i <= n; i++)
        {
            timeline.Key(i / (double)n, new Pose { Rotation = _frames[i] });
        }
        return timeline;
    }
}
