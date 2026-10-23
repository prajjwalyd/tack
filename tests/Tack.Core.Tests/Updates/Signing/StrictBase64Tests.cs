using Tack.Core.Updates.Signing;

namespace Tack.Core.Tests.Updates.Signing;

public class StrictBase64Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("AA==", "00")]
    [InlineData("AAA=", "0000")]
    [InlineData("AAAA", "000000")]
    [InlineData("dGVzdA==", "74657374")]
    [InlineData("+/+/", "fbffbf")]
    public void Strict_base64_decodes(string text, string hex)
    {
        Assert.Equal(hex, Convert.ToHexStringLower(StrictBase64.Decode(text)!));
    }

    [Theory]
    [InlineData("AA")]
    [InlineData("AAA")]
    [InlineData("AA=")]
    [InlineData("AA===")]
    [InlineData("A")]
    [InlineData("A===")]
    [InlineData("AB==")]
    [InlineData("AAB=")]
    [InlineData("AA==AA==")]
    [InlineData("AA AA")]
    [InlineData("AAAA\n")]
    [InlineData("-_-_")]
    [InlineData("AAAé")]
    public void Anything_else_is_refused(string text)
    {
        Assert.Null(StrictBase64.Decode(text));
    }

    [Fact]
    public void It_reads_what_the_platform_writes()
    {
        var bytes = new byte[300];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i * 7);
        }

        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.Equal(bytes[..length], StrictBase64.Decode(Convert.ToBase64String(bytes, 0, length)));
        }
    }
}
