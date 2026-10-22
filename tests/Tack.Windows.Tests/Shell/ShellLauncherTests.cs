using Tack.Windows.Shell;

namespace Tack.Windows.Tests.Shell;

public sealed class ShellLauncherTests
{
    [Theory]
    [InlineData("https://example.com/a?b=c")]
    [InlineData("http://example.com")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    public void Web_links_are_opened(string url) => Assert.True(ShellLauncher.IsWebLink(url));

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/\tcalc")]
    [InlineData("https:/example.com")]
    [InlineData("https:")]
    [InlineData("")]
    public void Anything_else_is_refused(string url) => Assert.False(ShellLauncher.IsWebLink(url));

    [Fact]
    public void A_look_alike_letter_is_not_an_s() =>
        // U+017F, the long s, folds to "S" under Unicode case rules.
        Assert.False(ShellLauncher.IsWebLink("http\u017f://example.com"));

    [Fact]
    public void Each_verb_goes_through_the_shell()
    {
        var info = ShellLauncher.StartInfo(@"C:\pics\a.png", "edit", lastTry: false);
        Assert.True(info.UseShellExecute);
        Assert.Equal("edit", info.Verb);
        Assert.Equal(@"C:\pics\a.png", info.FileName);
        Assert.Empty(info.Arguments);
    }

    [Fact]
    public void Only_the_last_try_may_show_an_error()
    {
        Assert.False(ShellLauncher.StartInfo(@"C:\pics\a.png", "edit", lastTry: false).ErrorDialog);
        Assert.True(ShellLauncher.StartInfo(@"C:\pics\a.png", "open", lastTry: true).ErrorDialog);
    }
}
