namespace Tack.Core.Pinboard.Motion;

/// <summary>Where a <see cref="Timeline"/>'s keyframes sit, and when its global easing reaches them.</summary>
static class TimelineOffsets
{
    /// <summary>
    /// Every keyframe's offset: the first defaults to 0, the last to 1 (a lone
    /// keyframe to 1), and a run left out between two known ones is spread
    /// evenly. Throws <see cref="ArgumentException"/> for none, an offset
    /// outside 0 to 1, or offsets out of order.
    /// </summary>
    public static double[] Compute(IReadOnlyList<Keyframe> keys)
    {
        if (keys.Count == 0)
        {
            throw new ArgumentException("A timeline needs at least one keyframe.", nameof(keys));
        }
        var offsets = keys.Select(k => k.Offset).ToArray();
        var last = -1.0;
        foreach (var offset in offsets.OfType<double>())
        {
            if (offset is < 0 or > 1 || double.IsNaN(offset) || offset < last)
            {
                throw new ArgumentException($"Keyframe offsets must be 0 to 1 and in order; got {offset}.", nameof(keys));
            }
            last = offset;
        }
        offsets[^1] ??= 1;
        offsets[0] ??= 0;
        var known = 0;
        for (var i = 1; i < offsets.Length; i++)
        {
            if (offsets[i] is not { } at)
            {
                continue;
            }
            var from = offsets[known]!.Value;
            for (var j = known + 1; j < i; j++)
            {
                offsets[j] = from + (at - from) * (j - known) / (i - known);
            }
            known = i;
        }
        return [.. offsets.Select(o => o!.Value)];
    }

    /// <summary>
    /// When, 0 to 1, <paramref name="easing"/> reaches progress
    /// <paramref name="point"/>: found by bisection, so for an easing that
    /// turns back it is one such moment. Null if it never gets there.
    /// </summary>
    public static double? TimeOf(Easing easing, double point)
    {
        if (ReferenceEquals(easing, Easing.Linear) || point is <= 0 or >= 1)
        {
            return Math.Clamp(point, 0, 1);
        }
        double lo = 0, hi = 1;
        for (var i = 0; i < 60; i++)
        {
            var mid = (lo + hi) / 2;
            if (easing.At(mid) < point)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        var t = (lo + hi) / 2;
        return Math.Abs(easing.At(t) - point) < 1e-6 ? t : null;
    }
}
