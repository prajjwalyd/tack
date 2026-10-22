using System.Globalization;
using Tack.Core.Prints;

namespace Tack.Core.Pinboard.Words;

/// <summary>
/// What a screen reader hears from the board: each print's name ("Screenshot,
/// 2 minutes ago, kept", "Link: github.com, just now"), the board's own name
/// and description, its buttons, and what is said when something happens
/// without the focus moving.
/// </summary>
public static class Labels
{
    /// <summary>The board's name: a list of prints.</summary>
    public const string Board = "Tack board";

    /// <summary>The board's description while it holds nothing.</summary>
    public const string EmptyBoard = "Empty. Take a screenshot with Windows key, Shift, S.";

    /// <summary>Said when a new print arrives with its pin-on.</summary>
    public const string Pinned = "Pinned";

    /// <summary>Said when a shown print falls off the board.</summary>
    public const string Unpinned = "Unpinned";

    /// <summary>Said when a print was copied to the clipboard.</summary>
    public const string Copied = "Copied";

    public const string UnpinButton = "Unpin";

    /// <summary>A note's words are cut after this many.</summary>
    public const int NoteWords = 8;

    /// <summary>Said when a print is kept, or no longer kept.</summary>
    public static string KeptChanged(bool kept) => kept ? "Kept" : "No longer kept";

    /// <summary>The Keep button's name, by whether the print is kept.</summary>
    public static string KeepButton(bool kept) => kept ? "Stop keeping" : "Keep";

    /// <summary>
    /// A print's name: what it is, how long ago it was pinned (spoken, as
    /// <see cref="Age.Spoken"/>), and whether it is kept.
    /// </summary>
    public static string For(Print print, long nowMs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        var note = print.IsNote ? print.Note : null;
        var what = "Screenshot";
        if (!string.IsNullOrEmpty(note?.Link))
        {
            what = $"Link: {(string.IsNullOrEmpty(note.Domain) ? note.Link : note.Domain)}";
        }
        else if (note is not null)
        {
            what = $"Note: {FirstWords(note.Text)}";
        }
        var kept = print.Kept ? ", kept" : string.Empty;
        return $"{what}, {Age.Spoken(print.PinnedAt, nowMs, culture, zone)}{kept}";
    }

    /// <summary>The first <paramref name="count"/> words of <paramref name="text"/> on one line, "…" if there were more; "empty" for none.</summary>
    public static string FirstWords(string? text, int count = NoteWords)
    {
        var words = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "empty";
        }
        var head = string.Join(' ', words.Take(count));
        return words.Length > count ? head + "…" : head;
    }
}
