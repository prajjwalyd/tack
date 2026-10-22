namespace Tack.Core.Phone.Qr;

/// <summary>
/// Splits the data codewords into blocks, the shorter ones first, adds each
/// block's error correction, and interleaves them in the order they are drawn.
/// </summary>
internal static class QrBlocks
{
    /// <summary>Every codeword to draw: the blocks' data a column at a time, then their error correction.</summary>
    public static byte[] Interleave(byte[] data, int version)
    {
        var count = QrVersion.Blocks(version);
        var ecLength = QrVersion.EcCodewordsPerBlock(version);
        var total = QrVersion.TotalCodewords(version);
        var shortBlocks = count - (total % count);
        var shortLength = (total / count) - ecLength;
        var divisor = ReedSolomon.Divisor(ecLength);

        var blocks = new byte[count][];
        var corrections = new byte[count][];
        for (int i = 0, at = 0; i < count; i++)
        {
            var length = shortLength + (i < shortBlocks ? 0 : 1);
            blocks[i] = data[at..(at + length)];
            corrections[i] = ReedSolomon.Remainder(blocks[i], divisor);
            at += length;
        }

        var codewords = new List<byte>(total);
        for (var i = 0; i <= shortLength; i++)
        {
            codewords.AddRange(from block in blocks where i < block.Length select block[i]);
        }
        for (var i = 0; i < ecLength; i++)
        {
            codewords.AddRange(from correction in corrections select correction[i]);
        }
        return [.. codewords];
    }
}
