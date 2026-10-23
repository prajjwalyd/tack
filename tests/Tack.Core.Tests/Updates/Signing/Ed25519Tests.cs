using System.Numerics;
using Tack.Core.Updates.Signing;

namespace Tack.Core.Tests.Updates.Signing;

public class Ed25519Tests
{
    [Theory]
    [MemberData(nameof(Rfc8032Vectors.All), MemberType = typeof(Rfc8032Vectors))]
    public void The_rfc_8032_vectors_verify(string name, string key, string message, string signature)
    {
        Assert.True(Ed25519.Verify(Bytes(key), Bytes(signature), Bytes(message)), name);
    }

    [Theory]
    [MemberData(nameof(Rfc8032Vectors.All), MemberType = typeof(Rfc8032Vectors))]
    public void A_flipped_message_bit_fails(string name, string key, string message, string signature)
    {
        var bytes = Bytes(message);
        if (bytes.Length == 0)
        {
            // Nothing to flip in TEST 1's empty message: one byte more changes it instead.
            Assert.False(Ed25519.Verify(Bytes(key), Bytes(signature), [0]), name);
            return;
        }

        foreach (var bit in new[] { 0, 7, (bytes.Length * 8) / 2, (bytes.Length * 8) - 1 })
        {
            Assert.False(Ed25519.Verify(Bytes(key), Bytes(signature), Flip(bytes, bit)), $"{name}, bit {bit}");
        }
    }

    [Theory]
    [MemberData(nameof(Rfc8032Vectors.All), MemberType = typeof(Rfc8032Vectors))]
    public void A_flipped_signature_bit_fails(string name, string key, string message, string signature)
    {
        // Bits of R (0 to 255) and of S (256 to 511), the top one included.
        foreach (var bit in new[] { 0, 100, 255, 256, 300, 500, 511 })
        {
            Assert.False(Ed25519.Verify(Bytes(key), Flip(Bytes(signature), bit), Bytes(message)), $"{name}, bit {bit}");
        }
    }

    [Theory]
    [MemberData(nameof(Rfc8032Vectors.All), MemberType = typeof(Rfc8032Vectors))]
    public void A_flipped_key_bit_fails(string name, string key, string message, string signature)
    {
        // The last bit is x's sign: flipped, the key is −A.
        foreach (var bit in new[] { 0, 1, 128, 254, 255 })
        {
            Assert.False(Ed25519.Verify(Flip(Bytes(key), bit), Bytes(signature), Bytes(message)), $"{name}, bit {bit}");
        }
    }

    [Fact]
    public void S_plus_the_group_order_is_refused()
    {
        // (R, S + L) satisfies the same equation as (R, S); only S < L is taken.
        var (key, message, signature) = Test1();
        var s = new BigInteger(signature.AsSpan(32), isUnsigned: true) + Ed25519.L;
        Assert.True(s.TryWriteBytes(signature.AsSpan(32), out _, isUnsigned: true));

        Assert.False(Ed25519.Verify(key, signature, message));
    }

    [Fact]
    public void The_identity_and_all_zero_keys_are_refused()
    {
        var (_, message, signature) = Test1();
        var identity = new byte[32];
        identity[0] = 1;
        var identityWithSign = (byte[])identity.Clone();
        identityWithSign[31] = 0x80;

        Assert.False(Ed25519.Verify(identity, signature, message));
        Assert.False(Ed25519.Verify(identityWithSign, signature, message));
        Assert.False(Ed25519.Verify(new byte[32], signature, message));
    }

    [Fact]
    public void Wrong_lengths_are_refused()
    {
        var (key, message, signature) = Test1();

        Assert.False(Ed25519.Verify(key[..31], signature, message));
        Assert.False(Ed25519.Verify(key, signature[..63], message));
        Assert.False(Ed25519.Verify([.. key, 0], signature, message));
        Assert.False(Ed25519.Verify(key, [.. signature, 0], message));
    }

    [Fact]
    public void The_base_point_encodes_as_rfc_8032_gives_it()
    {
        var encoded = new byte[32];
        EdwardsPoint.Base.Encode(encoded);

        Assert.Equal("58" + string.Concat(Enumerable.Repeat("66", 31)), Convert.ToHexStringLower(encoded));
    }

    [Fact]
    public void A_y_with_no_point_does_not_decode()
    {
        // y = 2 gives x² = 3 / (4d + 1), which is no square mod p; y = 3 has a point.
        Assert.Null(EdwardsPoint.Decode(Y(2)));
        Assert.NotNull(EdwardsPoint.Decode(Y(3)));
    }

    [Fact]
    public void A_y_at_or_above_p_is_taken_mod_p_as_tack_0_2_took_it()
    {
        // p + 3 = 2^255 − 16 names the same point as 3.
        var aboveP = (Field25519.P + 3).ToByteArray(isUnsigned: true);
        Assert.Equal(32, aboveP.Length);

        var expected = new byte[32];
        var actual = new byte[32];
        EdwardsPoint.Decode(Y(3))!.Value.Encode(expected);
        EdwardsPoint.Decode(aboveP)!.Value.Encode(actual);
        Assert.Equal(expected, actual);
    }

    static (byte[] Key, byte[] Message, byte[] Signature) Test1() =>
    (
        Bytes("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"),
        [],
        Bytes("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155" +
            "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b")
    );

    static byte[] Y(int y)
    {
        var bytes = new byte[32];
        bytes[0] = (byte)y;
        return bytes;
    }

    static byte[] Flip(byte[] bytes, int bit)
    {
        var copy = (byte[])bytes.Clone();
        copy[bit / 8] ^= (byte)(1 << (bit % 8));
        return copy;
    }

    static byte[] Bytes(string hex) => Convert.FromHexString(hex);
}
