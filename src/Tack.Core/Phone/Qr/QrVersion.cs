namespace Tack.Core.Phone.Qr;

/// <summary>What each version, 1 to 40, holds at error correction level M.</summary>
internal static class QrVersion
{
    public const int Largest = 40;

    static readonly int[] EcPerBlock =
    [
        10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26,
        26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
    ];

    static readonly int[] BlockCount =
    [
        1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16,
        17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49,
    ];

    /// <summary>Modules along a side.</summary>
    public static int Size(int version) => (version * 4) + 17;

    /// <summary>Error correction codewords in each block.</summary>
    public static int EcCodewordsPerBlock(int version) => EcPerBlock[version - 1];

    /// <summary>Blocks the codewords are split into.</summary>
    public static int Blocks(int version) => BlockCount[version - 1];

    /// <summary>Codewords of data and error correction together.</summary>
    public static int TotalCodewords(int version) => DataModules(version) / 8;

    /// <summary>Codewords of data, before error correction.</summary>
    public static int DataCodewords(int version) =>
        TotalCodewords(version) - (EcCodewordsPerBlock(version) * Blocks(version));

    /// <summary>The rows (and columns) alignment patterns are centred on.</summary>
    public static int[] AlignmentCenters(int version)
    {
        if (version == 1)
        {
            return [];
        }
        var count = (version / 7) + 2;
        var step = ((version * 8) + (count * 3) + 5) / ((count * 4) - 4) * 2;
        var centers = new int[count];
        centers[0] = 6;
        for (int i = count - 1, at = Size(version) - 7; i >= 1; i--, at -= step)
        {
            centers[i] = at;
        }
        return centers;
    }

    /// <summary>Modules left for data and error correction once the function patterns are drawn.</summary>
    static int DataModules(int version)
    {
        var modules = ((((version * 16) + 128) * version) + 64);
        if (version >= 2)
        {
            var alignments = (version / 7) + 2;
            modules -= ((((alignments * 25) - 10) * alignments) - 55);
            if (version >= 7)
            {
                modules -= 36;
            }
        }
        return modules;
    }
}
