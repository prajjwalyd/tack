namespace Tack.Core.Phone.Qr;

/// <summary>
/// The fixed parts of a code: finder, timing and alignment patterns, and the
/// format and version information that tell a reader how the rest is written.
/// </summary>
internal static class QrFunctionPatterns
{
    /// <summary>Draws every function pattern, with format information for mask 0 holding its place.</summary>
    public static void Draw(QrCanvas canvas, int version)
    {
        var size = canvas.Size;
        for (var i = 0; i < size; i++)
        {
            canvas.SetFunction(6, i, i % 2 == 0);
            canvas.SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(canvas, 3, 3);
        DrawFinder(canvas, size - 4, 3);
        DrawFinder(canvas, 3, size - 4);

        var centers = QrVersion.AlignmentCenters(version);
        var last = centers.Length - 1;
        for (var i = 0; i < centers.Length; i++)
        {
            for (var j = 0; j < centers.Length; j++)
            {
                var corner = (i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0);
                if (!corner)
                {
                    DrawAlignment(canvas, centers[i], centers[j]);
                }
            }
        }

        DrawFormat(canvas, 0);
        DrawVersion(canvas, version);
    }

    /// <summary>Draws both copies of the format information for level M and <paramref name="mask"/>.</summary>
    public static void DrawFormat(QrCanvas canvas, int mask)
    {
        // Level M is 00, so the five data bits are the mask alone.
        var bits = (mask << 10) | Remainder(mask, 10, 0x537);
        bits ^= 0x5412;

        var size = canvas.Size;
        for (var i = 0; i <= 5; i++)
        {
            canvas.SetFunction(8, i, Bit(bits, i));
        }
        canvas.SetFunction(8, 7, Bit(bits, 6));
        canvas.SetFunction(8, 8, Bit(bits, 7));
        canvas.SetFunction(7, 8, Bit(bits, 8));
        for (var i = 9; i < 15; i++)
        {
            canvas.SetFunction(14 - i, 8, Bit(bits, i));
        }

        for (var i = 0; i < 8; i++)
        {
            canvas.SetFunction(size - 1 - i, 8, Bit(bits, i));
        }
        for (var i = 8; i < 15; i++)
        {
            canvas.SetFunction(8, size - 15 + i, Bit(bits, i));
        }
        canvas.SetFunction(8, size - 8, true);
    }

    /// <summary>A 7×7 finder centred on (<paramref name="x"/>, <paramref name="y"/>) with its light border.</summary>
    static void DrawFinder(QrCanvas canvas, int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var (mx, my) = (x + dx, y + dy);
                if (mx >= 0 && mx < canvas.Size && my >= 0 && my < canvas.Size)
                {
                    var ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    canvas.SetFunction(mx, my, ring != 2 && ring != 4);
                }
            }
        }
    }

    /// <summary>A 5×5 alignment pattern centred on (<paramref name="x"/>, <paramref name="y"/>).</summary>
    static void DrawAlignment(QrCanvas canvas, int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                canvas.SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
    }

    /// <summary>From version 7, the version number twice, by the top right and bottom left finders.</summary>
    static void DrawVersion(QrCanvas canvas, int version)
    {
        if (version < 7)
        {
            return;
        }
        var bits = (version << 12) | Remainder(version, 12, 0x1F25);
        for (var i = 0; i < 18; i++)
        {
            var (along, across) = (canvas.Size - 11 + (i % 3), i / 3);
            canvas.SetFunction(along, across, Bit(bits, i));
            canvas.SetFunction(across, along, Bit(bits, i));
        }
    }

    /// <summary>The BCH check bits: <paramref name="data"/> shifted up <paramref name="length"/>, modulo the generator.</summary>
    static int Remainder(int data, int length, int generator)
    {
        var remainder = data;
        for (var i = 0; i < length; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> (length - 1)) * generator);
        }
        return remainder;
    }

    static bool Bit(int value, int index) => ((value >> index) & 1) != 0;
}
