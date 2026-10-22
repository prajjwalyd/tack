namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A keyframe animation as the board describes it: keyframes at offsets 0 to
/// 1, each with the easing toward the next; a global easing over the whole
/// run; a duration, a delay and a fill. <see cref="Sample"/> turns it into
/// dense frames per channel, to be played straight between them, so whoever
/// plays it never has to translate an easing. Each channel takes only the
/// keyframes that set it, and one missing at 0 or 1 is the channel's
/// underlying value. A global easing that overshoots carries on along the
/// first or last segment. Opacity stays within 0 to 1.
/// </summary>
/// <param name="duration">The active time, ms, already scaled for the speed.</param>
/// <param name="delay">Before it starts, ms.</param>
/// <param name="easing">Over the whole run; linear if null.</param>
public sealed class Timeline(double duration, double delay = 0, Easing? easing = null, Fill fill = Fill.None)
{
    /// <summary>Frames are at least this close: 120 a second.</summary>
    public const double DefaultFrameMs = 1000.0 / 120;

    readonly List<Keyframe> _keys = [];
    readonly Dictionary<Channel, double> _underlying = [];

    public double Duration => duration;

    public double Delay => delay;

    public Easing Easing { get; } = easing ?? Easing.Linear;

    public Fill Fill => fill;

    public IReadOnlyList<Keyframe> Keys => _keys;

    /// <summary>Adds a keyframe at <paramref name="offset"/> (null: spread evenly), easing toward the next one by <paramref name="easing"/>.</summary>
    public Timeline Key(double? offset, Pose pose, Easing? easing = null)
    {
        _keys.Add(new Keyframe(offset, pose, easing));
        return this;
    }

    /// <summary>The layer's own value for <paramref name="channel"/>, when it is not at rest.</summary>
    public Timeline Underlying(Channel channel, double value)
    {
        _underlying[channel] = value;
        return this;
    }

    public double UnderlyingOf(Channel channel) => _underlying.TryGetValue(channel, out var v) ? v : Pose.Rest(channel);

    /// <summary>
    /// The frames: progress points 0 to 1 at most <paramref name="frameMs"/>
    /// apart, plus every keyframe and every corner of an easing, with each
    /// channel's value there. Throws <see cref="ArgumentException"/> for no
    /// keyframes, or offsets out of order or outside 0 to 1.
    /// </summary>
    public SampledMotion Sample(double frameMs = DefaultFrameMs)
    {
        var offsets = TimelineOffsets.Compute(_keys);
        var channels = Enum.GetValues<Channel>().Where(c => _keys.Any(k => k.Pose.Explicit(c).HasValue)).ToArray();
        var keys = channels.ToDictionary(c => c, c => ChannelKeys(c, offsets));
        var frames = Frames(frameMs, offsets, keys.Values);
        var values = new Dictionary<Channel, double[]>();
        foreach (var channel in channels)
        {
            var row = new double[frames.Count];
            for (var i = 0; i < frames.Count; i++)
            {
                var v = Evaluate(keys[channel], frames[i].Progress);
                row[i] = channel == Channel.Opacity ? Math.Clamp(v, 0, 1) : v;
            }
            values[channel] = row;
        }
        var underlying = channels.ToDictionary(c => c, UnderlyingOf);
        var times = frames.Select(f => f.Time).ToArray();
        return new SampledMotion(duration, delay, fill, times, channels, values, underlying);
    }

    /// <summary>The keyframes that set <paramref name="channel"/>, with the underlying value standing in at a missing end.</summary>
    List<(double Offset, double Value, Easing Easing)> ChannelKeys(Channel channel, double[] offsets)
    {
        var keys = new List<(double Offset, double Value, Easing Easing)>();
        for (var i = 0; i < _keys.Count; i++)
        {
            if (_keys[i].Pose.ValueOf(channel) is { } value)
            {
                keys.Add((offsets[i], value, _keys[i].Easing ?? Easing.Linear));
            }
        }
        if (keys[0].Offset > 0)
        {
            keys.Insert(0, (0, UnderlyingOf(channel), Easing.Linear));
        }
        if (keys[^1].Offset < 1)
        {
            keys.Add((1, UnderlyingOf(channel), Easing.Linear));
        }
        return keys;
    }

    /// <summary>A channel's value at eased progress <paramref name="p"/>, by the keyframe interval it falls in.</summary>
    static double Evaluate(List<(double Offset, double Value, Easing Easing)> keys, double p)
    {
        if (p < 0 && keys.Count(k => k.Offset == 0) > 1)
        {
            return keys[0].Value;
        }
        if (p > 1 && keys.Count(k => k.Offset == 1) > 1)
        {
            return keys[^1].Value;
        }
        var start = keys.FindLastIndex(k => k.Offset <= p && k.Offset < 1);
        if (start < 0)
        {
            start = keys.FindLastIndex(k => k.Offset == 0);
        }
        var (from, to) = (keys[start], keys[start + 1]);
        var span = to.Offset - from.Offset;
        var distance = span == 0 ? 0 : (p - from.Offset) / span;
        // Past either end (an overshooting global easing) the segment carries on straight.
        var eased = distance is < 0 or > 1 ? distance : from.Easing.At(distance);
        return from.Value + (to.Value - from.Value) * eased;
    }

    /// <summary>
    /// A uniform grid, the corners of the global easing, and the moments each
    /// keyframe and each corner of a keyframe easing is reached, with the
    /// eased progress at each. At a keyframe the progress is its offset
    /// exactly, so its values land exactly.
    /// </summary>
    List<(double Time, double Progress)> Frames(
        double frameMs, double[] offsets, IEnumerable<List<(double Offset, double Value, Easing Easing)>> keys)
    {
        var count = Math.Max(1, (int)Math.Ceiling(duration / Math.Max(frameMs, 0.001)));
        var frames = new List<(double Time, double Progress)>(count + 8);
        foreach (var t in Enumerable.Range(0, count + 1).Select(i => i / (double)count).Concat(Easing.Knots))
        {
            frames.Add((t, Easing.At(t)));
        }
        var points = offsets.ToList();
        foreach (var channel in keys)
        {
            for (var i = 0; i + 1 < channel.Count; i++)
            {
                var (from, to) = (channel[i].Offset, channel[i + 1].Offset);
                points.AddRange(channel[i].Easing.Knots.Select(k => from + k * (to - from)));
            }
        }
        foreach (var point in points)
        {
            if (TimelineOffsets.TimeOf(Easing, point) is { } t)
            {
                frames.Add((t, point));
            }
        }
        // Stable, so a keyframe's exact progress follows a grid point at the same moment and wins below.
        var sorted = frames.Where(f => f.Time is >= 0 and <= 1).OrderBy(f => f.Time).ToList();
        var unique = new List<(double Time, double Progress)>(sorted.Count);
        foreach (var frame in sorted)
        {
            if (unique.Count > 0 && frame.Time - unique[^1].Time <= 1e-9)
            {
                unique[^1] = (unique[^1].Time, frame.Progress);
            }
            else
            {
                unique.Add(frame);
            }
        }
        unique[^1] = (1, unique[^1].Progress);
        return unique;
    }
}
