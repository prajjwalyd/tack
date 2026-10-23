using Tack.Core.Updates.Signing;

namespace Tack.Core.Tests.Updates.Signing;

public class SignedVersionTests
{
    [Theory]
    [InlineData("timestamp:1700000000\tfile:Tack_0.3.0_x64-setup.exe\tversion:0.3.0", "0.3.0")]
    [InlineData("version:0.2.1", "0.2.1")]
    [InlineData("version:1.0.0\ttimestamp:1700000000\tfile:Tack.exe", "1.0.0")]
    [InlineData("timestamp:1\tversion:0.3.0-beta.1\tfile:x", "0.3.0-beta.1")]
    [InlineData("timestamp:1\tversion:\tfile:x", "")]
    [InlineData("version:0.3.0\tversion:0.4.0", "0.3.0")]
    public void The_version_field_is_read(string comment, string expected)
    {
        Assert.Equal(expected, Minisign.SignedVersion(comment));
    }

    [Theory]
    [InlineData("timestamp:1700000000\tfile:Tack_0.2.0_x64-setup.exe")]
    [InlineData("")]
    [InlineData("Version:0.3.0")]
    [InlineData("file:version:0.3.0")]
    [InlineData("timestamp:1 version:0.3.0")]
    [InlineData(" version:0.3.0")]
    public void Without_a_version_field_there_is_none(string comment)
    {
        Assert.Null(Minisign.SignedVersion(comment));
    }
}
