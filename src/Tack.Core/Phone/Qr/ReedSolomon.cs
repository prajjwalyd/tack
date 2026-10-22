namespace Tack.Core.Phone.Qr;

/// <summary>Error correction codewords: the remainder of polynomial division over GF(256).</summary>
internal static class ReedSolomon
{
    /// <summary>The generator polynomial of <paramref name="degree"/>, highest term first, its leading 1 left out.</summary>
    public static byte[] Divisor(int degree)
    {
        var divisor = new byte[degree];
        divisor[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                divisor[j] = Multiply(divisor[j], root);
                if (j + 1 < degree)
                {
                    divisor[j] ^= divisor[j + 1];
                }
            }
            root = Multiply(root, 0x02);
        }
        return divisor;
    }

    /// <summary>The error correction codewords for <paramref name="data"/>.</summary>
    public static byte[] Remainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        var remainder = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ remainder[0];
            Array.Copy(remainder, 1, remainder, 0, remainder.Length - 1);
            remainder[^1] = 0;
            for (var i = 0; i < remainder.Length; i++)
            {
                remainder[i] ^= Multiply(divisor[i], factor);
            }
        }
        return remainder;
    }

    /// <summary>The product in GF(256) modulo x⁸ + x⁴ + x³ + x² + 1.</summary>
    static byte Multiply(int x, int y)
    {
        var product = 0;
        for (var i = 7; i >= 0; i--)
        {
            product = (product << 1) ^ ((product >> 7) * 0x11D);
            product ^= ((y >> i) & 1) * x;
        }
        return (byte)product;
    }
}
