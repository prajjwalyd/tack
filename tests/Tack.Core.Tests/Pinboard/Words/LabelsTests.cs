using System.Globalization;
using Tack.Core.Notes;
using Tack.Core.Pinboard.Words;
using Tack.Core.Prints;

namespace Tack.Core.Tests.Pinboard.Words;

/// <summary>Golden labels from Tack 0.2's label formula, run in node.</summary>
public class LabelsTests
{
    static readonly long Now = new DateTimeOffset(2026, 10, 22, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    static readonly long ThreeMinutesAgo = Now - 3 * 60_000;

    static Print Screenshot(bool kept = false) => new()
    {
        Id = "1",
        Path = "a.png",
        PinnedAt = ThreeMinutesAgo,
        KeptAt = kept ? Now : null,
    };

    static Print Note(string text, string? link = null, string? domain = null, bool kept = false) => Screenshot(kept) with
    {
        Kind = Kind.Note,
        Note = new NoteBody(text, link, domain, Truncated: false),
    };

    static string Label(Print print) => Labels.For(print, Now);

    [Fact]
    public void A_screenshot_says_what_it_is_its_age_and_kept()
    {
        Assert.Equal("Screenshot, 3 minutes ago", Label(Screenshot()));
        Assert.Equal("Screenshot, 3 minutes ago, kept", Label(Screenshot(kept: true)));
    }

    [Fact]
    public void A_note_says_its_first_eight_words_on_one_line()
    {
        Assert.Equal("Note: hello world again, 3 minutes ago", Label(Note("  hello   world \n again ")));
        Assert.Equal("Note: one two three four five six seven eight…, 3 minutes ago",
            Label(Note("one two three four five six seven eight nine ten")));
        Assert.Equal("Note: one two three four five six seven eight, 3 minutes ago",
            Label(Note("one two three four five six seven eight")));
        Assert.Equal("Note: empty, 3 minutes ago", Label(Note("   ")));
    }

    [Fact]
    public void A_link_says_its_domain_or_else_its_address()
    {
        Assert.Equal("Link: github.com, 3 minutes ago, kept",
            Label(Note("https://github.com/x", "https://github.com/x", "github.com", kept: true)));
        Assert.Equal("Link: https://github.com/x, 3 minutes ago", Label(Note("https://github.com/x", "https://github.com/x", "")));
        Assert.Equal("Link: https://github.com/x, 3 minutes ago", Label(Note("https://github.com/x", "https://github.com/x")));
    }

    [Fact]
    public void The_age_is_spoken_with_on_and_the_long_month()
    {
        var yesterday = Screenshot() with { PinnedAt = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() };
        Assert.Equal("Screenshot, yesterday", Labels.For(yesterday, Now, CultureInfo.GetCultureInfo("en-GB"), TimeZoneInfo.Utc));
        var older = Screenshot() with { PinnedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() };
        Assert.Equal("Screenshot, on 1 October", Labels.For(older, Now, CultureInfo.GetCultureInfo("en-GB"), TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(null, 8, "empty")]
    [InlineData("", 8, "empty")]
    [InlineData("a b c", 2, "a b…")]
    [InlineData("tab\tand nbsp", 8, "tab and nbsp")]
    public void First_words_split_on_any_white_space(string? text, int n, string expected)
    {
        Assert.Equal(expected, Labels.FirstWords(text, n));
    }
}
