namespace Tack.Core.Phone.Qr;

/// <summary>
/// Splits the text into segments as Tack 0.2 did: runs of digits, of the other
/// alphanumeric characters and of everything else, then, left to right, each
/// run joins the segment before it when one segment takes no more bits than two.
/// So "http://my-pc.netbird.cloud:7717" is bytes, then the port as digits.
/// </summary>
/// <remarks>
/// Every byte outside ASCII is a byte here. (Tack 0.2 sometimes wrote pairs of
/// UTF-8 bytes in kanji mode, which phones read back as other characters.)
/// </remarks>
internal static class QrSegmenter
{
    /// <summary>The segments for <paramref name="text"/>, sized with <paramref name="version"/>'s counts.</summary>
    public static List<QrSegment> Split(byte[] text, int version)
    {
        var segments = new List<QrSegment>();
        QrSegment? last = null;
        foreach (var run in Runs(text))
        {
            if (last is not { } before)
            {
                last = run;
                continue;
            }
            var joined = new QrSegment((QrMode)Math.Max((int)before.Mode, (int)run.Mode), before.Start, run.End);
            if (before.Bits(version) + run.Bits(version) >= joined.Bits(version))
            {
                last = joined;
            }
            else
            {
                segments.Add(before);
                last = run;
            }
        }
        if (last is { } end)
        {
            segments.Add(end);
        }
        return segments;
    }

    /// <summary>The longest runs of bytes that share the smallest mode that holds them.</summary>
    static IEnumerable<QrSegment> Runs(byte[] text)
    {
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || ModeOf(text[i]) != ModeOf(text[start]))
            {
                yield return new QrSegment(ModeOf(text[start]), start, i);
                start = i;
            }
        }
    }

    static QrMode ModeOf(byte b) =>
        b is >= (byte)'0' and <= (byte)'9' ? QrMode.Numeric
        : b < 0x80 && QrSegment.AlphanumericChars.Contains((char)b) ? QrMode.Alphanumeric
        : QrMode.Byte;
}
