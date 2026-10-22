using System.Globalization;

namespace Tack.Core.Pinboard.Words;

/// <summary>
/// How long ago a moment was, as the board's captions and screen-reader
/// labels say it: "just now", "5 minutes ago", "3 hours ago" (anything under
/// a day), "yesterday", the weekday within the week, then the date, with the
/// year only when it is not this year. The relative phrases are fixed English,
/// as the rest of Tack's words are; day and month names and the date's order
/// follow the culture. Days are counted in midnights in the local time zone.
/// Moments are ms since the Unix epoch.
/// </summary>
public static class Age
{
    const long MinuteMs = 60_000;
    const long HourMs = 60 * MinuteMs;

    /// <summary>
    /// How long before <paramref name="nowMs"/> <paramref name="thenMs"/> was.
    /// <paramref name="on"/> goes before a weekday or a date ("on Tuesday");
    /// <paramref name="longMonth"/> spells the date's month in full.
    /// </summary>
    /// <param name="culture">For day and month names; the current culture if null.</param>
    /// <param name="zone">Where midnight is; the local zone if null.</param>
    public static string Ago(
        long thenMs, long nowMs, string on = "", bool longMonth = false, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        var seconds = Math.Max(0, nowMs - thenMs) / 1000.0;
        if (seconds < 45)
        {
            return "just now";
        }
        var minutes = Math.Max(1, (long)Math.Floor(seconds / 60 + 0.5));
        if (minutes < 60)
        {
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }
        if (minutes < 24 * 60)
        {
            var hours = minutes / 60;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }
        zone ??= TimeZoneInfo.Local;
        culture ??= CultureInfo.CurrentCulture;
        var then = Local(thenMs, zone);
        var now = Local(nowMs, zone);
        var days = (int)Math.Round((now.Date - then.Date).TotalDays);
        if (days == 1)
        {
            return "yesterday";
        }
        if (days < 7)
        {
            return on + culture.DateTimeFormat.GetDayName(then.DayOfWeek);
        }
        return on + then.ToString(DatePattern(culture, longMonth, then.Year != now.Year), culture);
    }

    /// <summary>The caption under a hovered print: <see cref="Ago"/> with a capital ("Just now", "Yesterday", "12 Sep").</summary>
    public static string Caption(long thenMs, long nowMs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var text = Ago(thenMs, nowMs, culture: culture, zone: zone);
        return text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..];
    }

    /// <summary>The age as a screen reader says it: "just now", "yesterday", "on Tuesday", "on 12 September".</summary>
    public static string Spoken(long thenMs, long nowMs, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        Ago(thenMs, nowMs, on: "on ", longMonth: true, culture: culture, zone: zone);

    /// <summary>
    /// When a shown caption next needs new words, ms from <paramref name="nowMs"/>:
    /// under an hour old, just after the next whole minute of its age; else in
    /// five minutes. Class C: it follows the clock, not the speed.
    /// </summary>
    public static long RefreshMs(long thenMs, long nowMs)
    {
        var age = nowMs - thenMs;
        return age < HourMs ? MinuteMs - age % MinuteMs + 50 : 5 * MinuteMs;
    }

    static DateTime Local(long ms, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, zone);

    /// <summary>
    /// The culture's day and month in its own order, with the year when asked:
    /// its month-day pattern, or its long date without the weekday. The day
    /// is never padded; a short month is the abbreviated name.
    /// </summary>
    static string DatePattern(CultureInfo culture, bool longMonth, bool withYear)
    {
        var format = culture.DateTimeFormat;
        var pattern = withYear ? WithoutWeekday(format.LongDatePattern) : format.MonthDayPattern;
        pattern = Unpadded(pattern);
        return longMonth ? pattern : pattern.Replace("MMMM", "MMM", StringComparison.Ordinal);
    }

    /// <summary>A long date pattern without its weekday and the separator that went with it.</summary>
    static string WithoutWeekday(string pattern)
    {
        var at = pattern.IndexOf("dddd", StringComparison.Ordinal);
        if (at < 0)
        {
            return pattern;
        }
        var end = at + 4;
        while (end < pattern.Length && pattern[end] is ',' or ' ' or '،' or '、')
        {
            end++;
        }
        var start = at;
        if (end == pattern.Length)
        {
            // The weekday came last: drop the separator before it instead.
            while (start > 0 && pattern[start - 1] is ',' or ' ' or '،' or '、')
            {
                start--;
            }
        }
        return (pattern[..start] + pattern[end..]).Trim();
    }

    /// <summary>"dd" as "d", outside quoted literals: the day as a plain number.</summary>
    static string Unpadded(string pattern)
    {
        var chars = new System.Text.StringBuilder(pattern.Length);
        var quoted = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c is '\'' or '"')
            {
                quoted = !quoted;
            }
            if (!quoted && c == 'd' && i + 1 < pattern.Length && pattern[i + 1] == 'd'
                && (i + 2 >= pattern.Length || pattern[i + 2] != 'd') && (i == 0 || pattern[i - 1] != 'd'))
            {
                chars.Append('d');
                i++;
                continue;
            }
            chars.Append(c);
        }
        return chars.ToString();
    }
}
