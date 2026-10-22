using System.Globalization;
using Tack.Core.Pinboard.Words;

namespace Tack.Core.Tests.Pinboard.Words;

/// <summary>Golden phrases from Tack 0.2's age formula, run in node at a fixed moment in UTC.</summary>
public class AgeTests
{
    const long S = 1000, M = 60 * S, H = 60 * M;

    // Thursday 22 October 2026, 15:30 UTC.
    static readonly long Now = new DateTimeOffset(2026, 10, 22, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");
    static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    static long At(int year, int month, int day, int hour, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    static string Short(long then, CultureInfo culture) => Age.Ago(then, Now, culture: culture, zone: TimeZoneInfo.Utc);

    static string Long(long then, CultureInfo culture) =>
        Age.Ago(then, Now, on: "on ", longMonth: true, culture: culture, zone: TimeZoneInfo.Utc);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(44 * S, "just now")]
    [InlineData(45 * S, "1 minute ago")]
    [InlineData(89 * S, "1 minute ago")]
    [InlineData(90 * S, "2 minutes ago")]
    [InlineData(59 * M + 29 * S, "59 minutes ago")]
    [InlineData(59 * M + 30 * S, "1 hour ago")]
    [InlineData(60 * M, "1 hour ago")]
    [InlineData(119 * M, "1 hour ago")]
    [InlineData(23 * H + 59 * M, "23 hours ago")]
    [InlineData(-5 * S, "just now")]
    public void Under_a_day_is_in_fixed_english_whatever_the_culture(long age, string expected)
    {
        foreach (var culture in new[] { Gb, Us, CultureInfo.GetCultureInfo("de-DE"), CultureInfo.InvariantCulture })
        {
            Assert.Equal(expected, Short(Now - age, culture));
        }
    }

    [Fact]
    public void Days_count_midnights_in_the_local_zone()
    {
        Assert.Equal("15 hours ago", Short(At(2026, 10, 21, 23, 59), Gb));
        Assert.Equal("yesterday", Short(At(2026, 10, 21, 0), Gb));
        Assert.Equal("yesterday", Long(At(2026, 10, 21, 0), Gb));
        // Ten past midnight in Kolkata is still the day before in UTC.
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var then = At(2026, 10, 21, 18, 40);
        Assert.Equal("20 hours ago", Age.Ago(then, Now, zone: kolkata));
        Assert.Equal("yesterday", Age.Ago(then, Now + 24 * H, zone: kolkata));
        Assert.Equal("Wednesday", Age.Ago(then, Now + 24 * H, culture: Gb, zone: TimeZoneInfo.Utc));
    }

    [Fact]
    public void Within_the_week_is_the_weekday()
    {
        Assert.Equal("Tuesday", Short(At(2026, 10, 20, 12), Gb));
        Assert.Equal("on Tuesday", Long(At(2026, 10, 20, 12), Gb));
        Assert.Equal("Friday", Short(At(2026, 10, 16, 12), Us));
        Assert.Equal("on Friday", Long(At(2026, 10, 16, 12), Us));
    }

    [Fact]
    public void Older_is_the_date_in_the_cultures_order_with_the_year_only_when_not_this_year()
    {
        Assert.Equal("15 Oct", Short(At(2026, 10, 15, 12), Gb));
        Assert.Equal("on 15 October", Long(At(2026, 10, 15, 12), Gb));
        Assert.Equal("3 Jan", Short(At(2026, 1, 3, 12), Gb));
        Assert.Equal("on 3 January", Long(At(2026, 1, 3, 12), Gb));
        Assert.Equal("on 12 September 2025", Long(At(2025, 9, 12, 12), Gb));
        Assert.Equal("Oct 15", Short(At(2026, 10, 15, 12), Us));
        Assert.Equal("on January 3", Long(At(2026, 1, 3, 12), Us));
        Assert.Equal("Sep 12, 2025", Short(At(2025, 9, 12, 12), Us));
        Assert.Equal("on September 12, 2025", Long(At(2025, 9, 12, 12), Us));
    }

    [Fact]
    public void Day_and_month_names_follow_the_culture()
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("Dienstag", Short(At(2026, 10, 20, 12), de));
        Assert.Contains("Oktober", Long(At(2026, 10, 15, 12), de));
        Assert.Contains("2025", Short(At(2025, 9, 12, 12), de));
    }

    [Fact]
    public void A_caption_starts_with_a_capital_and_the_spoken_age_says_on()
    {
        Assert.Equal("Just now", Age.Caption(Now, Now));
        Assert.Equal("Yesterday", Age.Caption(At(2026, 10, 21, 0), Now, Gb, TimeZoneInfo.Utc));
        Assert.Equal("2 minutes ago", Age.Spoken(Now - 2 * M, Now));
        Assert.Equal("on Tuesday", Age.Spoken(At(2026, 10, 20, 12), Now, Gb, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(0, 60 * S + 50)]
    [InlineData(30 * S, 30 * S + 50)]
    [InlineData(59 * M + 59 * S, 1 * S + 50)]
    [InlineData(60 * M, 5 * M)]
    [InlineData(30 * H, 5 * M)]
    public void A_caption_under_an_hour_old_refreshes_just_after_the_next_minute_else_every_five(long age, long expected)
    {
        Assert.Equal(expected, Age.RefreshMs(Now - age, Now));
    }
}
