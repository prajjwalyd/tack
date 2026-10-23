using System.Numerics;

namespace Tack.Core.Updates.Signing;

/// <summary>
/// Arithmetic modulo p = 2^255 − 19, the field Ed25519's coordinates live in.
/// Every value going in and coming out is reduced, in [0, p). Verification
/// handles only public data, so plain big integers do: speed and constant
/// time do not matter here, being plainly right does.
/// </summary>
internal static class Field25519
{
    /// <summary>The prime p = 2^255 − 19.</summary>
    public static readonly BigInteger P = (BigInteger.One << 255) - 19;

    /// <summary>The curve constant d = −121665 / 121666.</summary>
    public static readonly BigInteger D = Mul(Reduce(-121665), Invert(121666));

    /// <summary>A square root of −1: 2^((p−1)/4), since 2 is not a square mod p.</summary>
    public static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);

    /// <summary><paramref name="a"/> mod p, in [0, p) even when <paramref name="a"/> is negative.</summary>
    public static BigInteger Reduce(BigInteger a)
    {
        var r = a % P;
        return r.Sign < 0 ? r + P : r;
    }

    public static BigInteger Add(BigInteger a, BigInteger b) => Reduce(a + b);

    public static BigInteger Sub(BigInteger a, BigInteger b) => Reduce(a - b);

    public static BigInteger Mul(BigInteger a, BigInteger b) => Reduce(a * b);

    public static BigInteger Pow(BigInteger a, BigInteger exponent) => BigInteger.ModPow(a, exponent, P);

    /// <summary>1/a, as a^(p−2) (Fermat); 0 has no inverse and gives 0.</summary>
    public static BigInteger Invert(BigInteger a) => Pow(a, P - 2);

    /// <summary>Whether <paramref name="a"/> counts as negative in an encoding: whether it is odd.</summary>
    public static bool IsNegative(BigInteger a) => !a.IsEven;

    /// <summary>
    /// The field element in the low 255 bits of 32 little-endian bytes; the top
    /// bit is the x sign of a point encoding and is ignored. Values from p to
    /// 2^255 − 1 are taken mod p rather than refused, as Tack 0.2 took them.
    /// </summary>
    public static BigInteger FromBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> low = stackalloc byte[32];
        bytes[..32].CopyTo(low);
        low[31] &= 0x7F;
        return Reduce(new BigInteger(low, isUnsigned: true, isBigEndian: false));
    }

    /// <summary>Writes <paramref name="a"/> (reduced) as 32 little-endian bytes, the top bit clear.</summary>
    public static void ToBytes(BigInteger a, Span<byte> bytes)
    {
        bytes[..32].Clear();
        a.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
    }
}
