using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;

namespace Tack.Core.Updates.Signing;

/// <summary>
/// Ed25519 signature verification (RFC 8032), the check minisign relies on.
/// Verification only: Tack never signs. It refuses what Tack 0.2 refused:
/// an S not below the group order, the identity or an all-zero public key, a
/// public key that is no point, and any R that is not exactly the encoding
/// the check computes.
/// </summary>
internal static class Ed25519
{
    public const int PublicKeySize = 32;
    public const int SignatureSize = 64;

    /// <summary>L, the order of the base point: 2^252 + 27742317777372353535851937790883648493.</summary>
    public static readonly BigInteger L =
        (BigInteger.One << 252) + BigInteger.Parse("27742317777372353535851937790883648493", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether <paramref name="signature"/> (R ‖ S, 64 bytes) is <paramref name="publicKey"/>'s
    /// (A, 32 bytes) signature over <paramref name="message"/>.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> message)
    {
        if (publicKey.Length != PublicKeySize || signature.Length != SignatureSize)
        {
            return false;
        }

        var encodedR = signature[..32];
        var s = new BigInteger(signature[32..], isUnsigned: true, isBigEndian: false);

        // S must be fully reduced, else (R, S + L) would verify as well as (R, S).
        if (s >= L || IsIdentity(publicKey) || !publicKey.ContainsAnyExcept((byte)0))
        {
            return false;
        }

        if (EdwardsPoint.Decode(publicKey) is not { } a)
        {
            return false;
        }

        // k = SHA-512(R ‖ A ‖ message) mod L.
        Span<byte> digest = stackalloc byte[SHA512.HashSizeInBytes];
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA512))
        {
            sha.AppendData(encodedR);
            sha.AppendData(publicKey);
            sha.AppendData(message);
            sha.GetHashAndReset(digest);
        }

        var k = new BigInteger(digest, isUnsigned: true, isBigEndian: false) % L;

        // The cofactorless check: [S]B − [k]A, encoded, must be R byte for byte.
        var r = EdwardsPoint.DoubleMultiply(k, a.Negate(), s);
        Span<byte> encoded = stackalloc byte[32];
        r.Encode(encoded);
        return encoded.SequenceEqual(encodedR);
    }

    /// <summary>Whether the key encodes the neutral point (y = 1), whatever its sign bit says.</summary>
    static bool IsIdentity(ReadOnlySpan<byte> publicKey)
    {
        var bits = publicKey[0] ^ 1;
        for (var i = 1; i < 31; i++)
        {
            bits |= publicKey[i];
        }

        bits |= publicKey[31] & 0x7F;
        return bits == 0;
    }
}
