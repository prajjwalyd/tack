using Tack.Core.Prints;

namespace Tack.Core.Pinboard.Layout;

/// <summary>
/// A print's size on the board, logical px. A photo fits 160 by 100, never
/// under 40 by 30, plus an even paper border; a picture of unknown size shows
/// 16:9 at the widest. A note is one fixed size, with no border, and so is a
/// linked file's card.
/// </summary>
public static class PrintSize
{
    /// <summary>The print's whole size, the paper border included, for a picture <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static (double W, double H) Of(Kind kind, int width, int height)
    {
        if (kind == Kind.Note)
        {
            return (BoardMetrics.NoteWidth, BoardMetrics.NoteHeight);
        }
        if (kind == Kind.File)
        {
            return (BoardMetrics.FileWidth, BoardMetrics.FileHeight);
        }
        var (w, h) = Photo(width, height);
        return (w + 2 * BoardMetrics.PrintBorder, h + 2 * BoardMetrics.PrintBorder);
    }

    /// <summary>The photo alone for a picture <paramref name="width"/> by <paramref name="height"/>, whole px.</summary>
    public static (double W, double H) Photo(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return (BoardMetrics.UnknownWidth, BoardMetrics.UnknownHeight);
        }
        var s = Math.Min(BoardMetrics.PhotoMaxWidth / width, BoardMetrics.PhotoMaxHeight / height);
        return (
            HalfUp.Round(Math.Max(BoardMetrics.PhotoMin, width * s)),
            HalfUp.Round(Math.Max(BoardMetrics.PhotoMinHeight, height * s)));
    }
}
