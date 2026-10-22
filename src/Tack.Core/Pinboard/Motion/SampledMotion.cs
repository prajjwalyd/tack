namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A <see cref="Timeline"/> as frames: progress points 0 to 1 shared by every
/// channel, and each channel's value at each, to be played straight between
/// them over <see cref="Duration"/> after <see cref="Delay"/>.
/// </summary>
public sealed class SampledMotion
{
    readonly Dictionary<Channel, double[]> _values;
    readonly Dictionary<Channel, double> _underlying;

    internal SampledMotion(
        double duration, double delay, Fill fill, double[] times, Channel[] channels,
        Dictionary<Channel, double[]> values, Dictionary<Channel, double> underlying)
    {
        (Duration, Delay, Fill, Times, Channels) = (duration, delay, fill, times, channels);
        (_values, _underlying) = (values, underlying);
    }

    /// <summary>The active time, ms.</summary>
    public double Duration { get; }

    public double Delay { get; }

    public Fill Fill { get; }

    /// <summary>The frames' places, 0 to 1, strictly increasing, from exactly 0 to exactly 1.</summary>
    public IReadOnlyList<double> Times { get; }

    /// <summary>The channels moved, in <see cref="Channel"/> order.</summary>
    public IReadOnlyList<Channel> Channels { get; }

    /// <summary>The channel's value at each of <see cref="Times"/>.</summary>
    public IReadOnlyList<double> this[Channel channel] => _values[channel];

    /// <summary>What the layer shows for the channel when the motion does not: before a delay unfilled, after an end unfilled.</summary>
    public double Underlying(Channel channel) => _underlying[channel];

    /// <summary>The channel's value at progress <paramref name="t"/>, straight between frames.</summary>
    public double ValueAt(Channel channel, double t)
    {
        var values = _values[channel];
        if (t <= 0)
        {
            return values[0];
        }
        if (t >= 1)
        {
            return values[^1];
        }
        var i = 1;
        while (Times[i] < t)
        {
            i++;
        }
        var (t0, t1) = (Times[i - 1], Times[i]);
        return values[i - 1] + (values[i] - values[i - 1]) * (t - t0) / (t1 - t0);
    }
}
