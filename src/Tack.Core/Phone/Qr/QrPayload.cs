namespace Tack.Core.Phone.Qr;

/// <summary>The text as data codewords, in the smallest version that holds them.</summary>
internal sealed record QrPayload(int Version, byte[] Codewords)
{
    /// <summary>Character counts take more bits from versions 10 and 27, so each band is tried in turn.</summary>
    static readonly int[] BandTops = [9, 26, QrVersion.Largest];

    /// <summary>The payload for <paramref name="text"/>, or null when even version 40 is too small.</summary>
    public static QrPayload? Of(byte[] text)
    {
        foreach (var top in BandTops)
        {
            var segments = QrSegmenter.Split(text, top);
            var needed = segments.Sum(segment => segment.Bits(top));
            if (needed <= QrVersion.DataCodewords(top) * 8)
            {
                var version = 1;
                while (QrVersion.DataCodewords(version) * 8 < needed)
                {
                    version++;
                }
                return new QrPayload(version, Write(text, segments, version));
            }
        }
        return null;
    }

    /// <summary>The segments, a terminator of up to four zeros, then alternate pad bytes to fill the version.</summary>
    static byte[] Write(byte[] text, List<QrSegment> segments, int version)
    {
        var bits = new QrBits();
        foreach (var segment in segments)
        {
            segment.Write(bits, text, version);
        }
        var capacity = QrVersion.DataCodewords(version) * 8;
        bits.Append(0, Math.Min(4, capacity - bits.Count));
        bits.Append(0, (8 - (bits.Count % 8)) % 8);
        for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
        {
            bits.Append(pad, 8);
        }
        return bits.ToBytes();
    }
}
