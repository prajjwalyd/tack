using Windows.UI;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The colours a note prints with in the current look: the paper, and the link
/// and its domain on it (worked out by <see cref="BoardTheme"/> from the note paper).
/// </summary>
readonly record struct NoteTones(Color Paper, Color Link, Color Domain)
{
    public static NoteTones Of(BoardTheme theme) => new(theme.NotePaper, theme.NoteLink, theme.NoteDomain);
}
