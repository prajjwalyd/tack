using System.Numerics;
using static Tack.Core.Updates.Signing.Field25519;

namespace Tack.Core.Updates.Signing;

/// <summary>
/// A point on Ed25519's curve, −x² + y² = 1 + d·x²·y², in extended
/// coordinates (X : Y : Z : T) with x = X/Z, y = Y/Z and x·y = T/Z, which let
/// points be added without a division each time (RFC 8032, section 5.1.4).
/// </summary>
internal readonly struct EdwardsPoint
{
    readonly BigInteger _x;
    readonly BigInteger _y;
    readonly BigInteger _z;
    readonly BigInteger _t;

    EdwardsPoint(BigInteger x, BigInteger y, BigInteger z, BigInteger t)
    {
        _x = x;
        _y = y;
        _z = z;
        _t = t;
    }

    /// <summary>The neutral point, (0, 1).</summary>
    public static readonly EdwardsPoint Identity = new(0, 1, 1, 0);

    /// <summary>The base point B: y = 4/5, with the x that is even ("positive").</summary>
    public static readonly EdwardsPoint Base = Recover(Mul(4, Invert(5)), xOdd: false)
        ?? throw new InvalidOperationException("4/5 is the y of a point.");

    /// <summary>
    /// The point a 32-byte encoding names: y in the low 255 bits, the sign
    /// (oddness) of x in the top bit. Null when no point has that y. As in
    /// Tack 0.2, y need not be below p, and x = 0 with the sign bit set is
    /// taken as x = 0 rather than refused.
    /// </summary>
    public static EdwardsPoint? Decode(ReadOnlySpan<byte> encoded) =>
        Recover(FromBytes(encoded), xOdd: (encoded[31] & 0x80) != 0);

    /// <summary>The point with this y and an x of the given oddness, if the curve has one.</summary>
    static EdwardsPoint? Recover(BigInteger y, bool xOdd)
    {
        // From the curve equation, x² = u/v with u = y² − 1 and v = d·y² + 1.
        var yy = Mul(y, y);
        var u = Sub(yy, 1);
        var v = Add(Mul(D, yy), 1);

        // A candidate root, x = u·(u·v)^((p−5)/8). When u/v is a square,
        // v·x² comes out as u or as −u; in the second case x·√−1 is the root.
        var x = Mul(u, Pow(Mul(u, v), (P - 5) / 8));
        var vxx = Mul(v, Mul(x, x));
        if (vxx != u)
        {
            if (vxx != Sub(0, u))
            {
                return null;
            }

            x = Mul(x, SqrtMinusOne);
        }

        // Of the two roots x and −x, take the one whose oddness the encoding gave.
        if (IsNegative(x) != xOdd)
        {
            x = Sub(0, x);
        }

        return new EdwardsPoint(x, y, 1, Mul(x, y));
    }

    /// <summary>The 32-byte encoding: y = Y/Z little-endian, with the oddness of x = X/Z in the top bit.</summary>
    public void Encode(Span<byte> encoded)
    {
        var zInverse = Invert(_z);
        var x = Mul(_x, zInverse);
        var y = Mul(_y, zInverse);
        ToBytes(y, encoded);
        if (IsNegative(x))
        {
            encoded[31] |= 0x80;
        }
    }

    /// <summary>−P = (−x, y).</summary>
    public EdwardsPoint Negate() => new(Sub(0, _x), _y, _z, Sub(0, _t));

    /// <summary>
    /// P + Q, by RFC 8032's addition formulas for a = −1. They are complete on
    /// this curve: right for any two points, P + P included, so they double too.
    /// </summary>
    public EdwardsPoint Plus(EdwardsPoint q)
    {
        var a = Mul(Sub(_y, _x), Sub(q._y, q._x));
        var b = Mul(Add(_y, _x), Add(q._y, q._x));
        var c = Mul(Mul(2, D), Mul(_t, q._t));
        var d = Mul(2, Mul(_z, q._z));
        var e = Sub(b, a);
        var f = Sub(d, c);
        var g = Add(d, c);
        var h = Add(b, a);
        return new EdwardsPoint(Mul(e, f), Mul(g, h), Mul(f, g), Mul(e, h));
    }

    /// <summary>
    /// [a]P + [b]B for non-negative scalars below 2^256, with one shared run of
    /// doublings (Straus's method): from the top bit down, double, then add P
    /// and B where a and b have that bit set.
    /// </summary>
    public static EdwardsPoint DoubleMultiply(BigInteger a, EdwardsPoint p, BigInteger b)
    {
        Span<byte> aBytes = stackalloc byte[32];
        Span<byte> bBytes = stackalloc byte[32];
        ToScalarBytes(a, aBytes);
        ToScalarBytes(b, bBytes);

        var r = Identity;
        for (var bit = 255; bit >= 0; bit--)
        {
            r = r.Plus(r);
            if (IsSet(aBytes, bit))
            {
                r = r.Plus(p);
            }

            if (IsSet(bBytes, bit))
            {
                r = r.Plus(Base);
            }
        }

        return r;
    }

    static void ToScalarBytes(BigInteger scalar, Span<byte> bytes)
    {
        if (scalar.Sign < 0 || !scalar.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false))
        {
            throw new ArgumentOutOfRangeException(nameof(scalar), "A scalar must be in [0, 2^256).");
        }
    }

    static bool IsSet(ReadOnlySpan<byte> littleEndian, int bit) => ((littleEndian[bit >> 3] >> (bit & 7)) & 1) != 0;
}
