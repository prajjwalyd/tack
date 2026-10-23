using Tack.Core.Updates.Signing;

namespace Tack.Core.Tests.Updates.Signing;

/// <summary>
/// BLAKE2b-512. "abc" is RFC 7693's own example (appendix A); the other
/// expected values come from Python 3.12's hashlib.blake2b, over bytes
/// i % 251 for i from 0, at lengths around the 128-byte block.
/// </summary>
public class Blake2bTests
{
    [Fact]
    public void The_rfc_7693_example_hashes_as_published()
    {
        Assert.Equal(
            "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d1" +
            "7d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923",
            Hex(Blake2b.Hash("abc"u8)));
    }

    [Fact]
    public void Nothing_hashes_to_the_hash_of_one_empty_block()
    {
        Assert.Equal(
            "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419" +
            "d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce",
            Hex(Blake2b.Hash([])));
    }

    [Theory]
    [InlineData(1, "2fa3f686df876995167e7c2e5d74c4c7b6e48f8068fe0e44208344d480f7904c36963e44115fe3eb2a3ac8694c28bcb4f5a0f3276f2e79487d8219057a506e4b")]
    [InlineData(127, "b6292669ccd38d5f01caae96ba272c76a879a45743afa0725d83b9ebb26665b731f1848c52f11972b6644f554c064fa90780dbbbf3a89d4fc31f67df3e5857ef")]
    [InlineData(128, "2319e3789c47e2daa5fe807f61bec2a1a6537fa03f19ff32e87eecbfd64b7e0e8ccff439ac333b040f19b0c4ddd11a61e24ac1fe0f10a039806c5dcc0da3d115")]
    [InlineData(129, "f59711d44a031d5f97a9413c065d1e614c417ede998590325f49bad2fd444d3e4418be19aec4e11449ac1a57207898bc57d76a1bcf3566292c20c683a5c4648f")]
    [InlineData(255, "fe2c02da499516b0e9fb2dd70c49eb3629039f632e20a880946fb7bc97a7ab09deb7d48774d7f0648141c9d9ede19ae6e0dbf07863a128cf4b00195f0f179f74")]
    [InlineData(256, "93463ac058b6163eb43be3f5bb32b28541498f4e3366f1effe253ad44e1e076e41c3616046027c82a7124f8f4746668ad10b12e8e25a95ac8f3151df01cd5a93")]
    [InlineData(1000, "c11e1c0340bd7e5a1b275f1230c962fad215ecb1391486e74e31b960a2f2996381a5fad092da06841d5f26e38f6ecfeaf441acbcd1c2de61aef121e7927175f5")]
    [InlineData(100_000, "a0b4e1c912a57364ed019d57296b9f14fd4ac7a36dc5b324706ba2b1497cf7b58b813ef6d86efab3a9461768ea9a552ab97554f8c4b010ed26d04ae6158bcedf")]
    public void Longer_inputs_hash_as_python_hashes_them(int length, string expected)
    {
        Assert.Equal(expected, Hex(Blake2b.Hash(Pattern(length))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(4096)]
    public void Pieces_hash_the_same_as_the_whole(int piece)
    {
        var data = Pattern(100_000);
        var hasher = new Blake2b();
        for (var at = 0; at < data.Length; at += piece)
        {
            hasher.Update(data.AsSpan(at, Math.Min(piece, data.Length - at)));
        }

        var hash = new byte[Blake2b.HashSize];
        hasher.Finish(hash);
        Assert.Equal(Blake2b.Hash(data), hash);
    }

    [Fact]
    public void Empty_pieces_change_nothing()
    {
        var hasher = new Blake2b();
        hasher.Update([]);
        hasher.Update("abc"u8);
        hasher.Update([]);
        var hash = new byte[Blake2b.HashSize];
        hasher.Finish(hash);
        Assert.Equal(Blake2b.Hash("abc"u8), hash);
    }

    static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        return data;
    }

    static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
