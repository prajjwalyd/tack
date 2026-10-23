using Tack.Core.Updates.Signing;

namespace Tack.Core.Tests.Updates.Signing;

public class TextLinesTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a\n", new[] { "a" })]
    [InlineData("a\nb", new[] { "a", "b" })]
    [InlineData("a\r\nb\r\n", new[] { "a", "b" })]
    [InlineData("a\n\nb", new[] { "a", "", "b" })]
    [InlineData("\n", new[] { "" })]
    [InlineData("a\rb\r", new[] { "a\rb\r" })]
    [InlineData("a\r\r\nb", new[] { "a\r", "b" })]
    public void Lines_split_at_lf_or_crlf(string text, string[] expected)
    {
        Assert.Equal(expected, TextLines.Split(text));
    }
}
