using Tack.Core.Updates;

namespace Tack.Core.Tests.Updates;

public class SemVersionTests
{
    static SemVersion V(string text) => SemVersion.TryParse(text, out var version) ? version : throw new FormatException(text);

    [Theory]
    [InlineData("0.2.1", "0.3.0")]
    [InlineData("0.3.0", "0.3.1")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("1.9.0", "10.0.0")]
    [InlineData("0.3.0-beta.1", "0.3.0")]
    [InlineData("0.3.0-alpha", "0.3.0-alpha.1")]
    [InlineData("0.3.0-alpha.1", "0.3.0-alpha.beta")]
    [InlineData("0.3.0-beta.2", "0.3.0-beta.10")]
    [InlineData("0.3.0-beta.11", "0.3.0-rc.1")]
    [InlineData("0.2.9", "0.3.0-beta.1")]
    public void Versions_are_in_semver_order(string lower, string higher)
    {
        Assert.True(V(lower) < V(higher), $"{lower} < {higher}");
        Assert.True(V(higher) > V(lower), $"{higher} > {lower}");
        Assert.NotEqual(V(lower), V(higher));
    }

    [Fact]
    public void A_leading_v_is_tolerated()
    {
        Assert.Equal(V("0.3.1"), V("v0.3.1"));
        Assert.Equal("0.3.1-beta.1", V("v0.3.1-beta.1").ToString());
        Assert.Equal(0, V("v1.2.3").CompareTo(V("1.2.3")));
    }

    [Fact]
    public void A_version_reads_back_as_written()
    {
        var version = V("1.20.300-rc.1+build.5");
        Assert.Equal((1UL, 20UL, 300UL, "rc.1", "build.5"), (version.Major, version.Minor, version.Patch, version.Pre, version.Build));
        Assert.Equal("1.20.300-rc.1+build.5", version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.3")]
    [InlineData("0.3.0.1")]
    [InlineData("01.3.0")]
    [InlineData("0.3.x")]
    [InlineData("0.3.0-")]
    [InlineData("0.3.0-beta..1")]
    [InlineData("0.3.0-beta.01")]
    [InlineData("0.3.0+")]
    [InlineData(" 0.3.0")]
    [InlineData("-1.0.0")]
    [InlineData("0.3.0-bêta")]
    public void Anything_else_is_not_a_version(string? text) => Assert.False(SemVersion.TryParse(text, out _));
}
