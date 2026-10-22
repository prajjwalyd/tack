using Tack.Core.Geometry;

namespace Tack.Core.Pinboard.Layout;

/// <summary>
/// The row laid out: one row of prints, kept ones first with a wider gap
/// after them, and the board's width for it. The board fits the row up to the
/// window less <see cref="BoardMetrics.Side"/>, never under
/// <see cref="BoardMetrics.MinWidth"/>; a longer row scrolls. Each print's x is
/// relative to the board's centre, so a width change never moves prints
/// already placed. Scroll positions are logical px from the row's start, 0 to
/// <see cref="ScrollExtent"/>.
/// </summary>
public sealed class RowLayout
{
    /// <summary>A print shown by scrolling clears the view's edge by this much.</summary>
    public const double ShowMargin = 24;

    readonly RowEntry[] _entries;
    readonly double[] _x;

    /// <param name="entries">The prints in row order.</param>
    /// <param name="window">The window's width, logical px; 0 or less if not known yet, which never limits the board.</param>
    public RowLayout(IReadOnlyList<RowEntry> entries, double window)
    {
        _entries = [.. entries];
        var offsets = new double[_entries.Length];
        var cursor = 0.0;
        for (var i = 0; i < _entries.Length; i++)
        {
            if (i > 0)
            {
                cursor += _entries[i - 1].Kept && !_entries[i].Kept ? BoardMetrics.GroupGap : BoardMetrics.Gap;
            }
            offsets[i] = cursor;
            cursor += _entries[i].Width;
        }
        var widest = window > 0 ? Math.Max(BoardMetrics.MinWidth, window - BoardMetrics.Side) : double.PositiveInfinity;
        ContentWidth = _entries.Length > 0 ? cursor + 2 * BoardMetrics.Pad : 0;
        BoardWidth = HalfUp.Round(Math.Min(widest, Math.Max(BoardMetrics.MinWidth, ContentWidth)));
        Fits = ContentWidth <= BoardWidth;
        var start = (Fits ? -ContentWidth : -BoardWidth) / 2 + BoardMetrics.Pad;
        _x = [.. offsets.Select(offset => HalfUp.Round(start + offset))];
        ScrollExtent = Math.Max(0, HalfUp.Round(ContentWidth - BoardWidth));
    }

    /// <summary>Prints hang from this line, below the screen edge.</summary>
    public static double Y => BoardMetrics.PrintTop;

    public int Count => _entries.Length;

    public IReadOnlyList<RowEntry> Entries => _entries;

    /// <summary>Each print's left edge, relative to the board's centre, whole px.</summary>
    public IReadOnlyList<double> X => _x;

    /// <summary>The prints, the gaps between them and the felt either side.</summary>
    public double ContentWidth { get; }

    /// <summary>The board's width, whole px: also the row's visible width.</summary>
    public double BoardWidth { get; }

    /// <summary>The whole row shows; it does not scroll.</summary>
    public bool Fits { get; }

    /// <summary>How far the row can scroll, whole px; 0 when it fits.</summary>
    public double ScrollExtent { get; }

    /// <summary>A scroll position kept within 0 to <see cref="ScrollExtent"/>.</summary>
    public double Clamp(double scroll) => Math.Max(0, Math.Min(ScrollExtent, scroll));

    /// <summary>Print <paramref name="index"/>'s left and right edges in scroll space: where they are at scroll 0, from the view's left.</summary>
    public (double Left, double Right) Span(int index)
    {
        var left = _x[index] + BoardWidth / 2;
        return (left, left + _entries[index].Width);
    }

    /// <summary>Whether some of print <paramref name="index"/> shows with the row at <paramref name="scroll"/>.</summary>
    public bool InView(int index, double scroll)
    {
        if (Fits)
        {
            return true;
        }
        var (left, right) = Span(index);
        return right > scroll && left < BoardWidth + scroll;
    }

    /// <summary>
    /// Where the row should go, from <paramref name="target"/>, for print
    /// <paramref name="index"/> to show whole with <see cref="ShowMargin"/>
    /// to spare: <paramref name="target"/> itself if it already does, or if the row fits.
    /// </summary>
    public double ScrollToShow(int index, double target)
    {
        if (Fits)
        {
            return target;
        }
        var (left, right) = Span(index);
        if (left - ShowMargin < target)
        {
            return Clamp(left - ShowMargin);
        }
        return right + ShowMargin > target + BoardWidth ? Clamp(right + ShowMargin - BoardWidth) : target;
    }

    /// <summary>
    /// Where the row rests when the board comes down: its start, or, when
    /// kept prints fill the view, just far enough to show the first print that is not kept.
    /// </summary>
    public double RestScroll()
    {
        var first = Array.FindIndex(_entries, entry => !entry.Kept);
        if (Fits || first < 0)
        {
            return 0;
        }
        var right = Span(first).Right + BoardMetrics.Pad;
        return right > BoardWidth ? Clamp(right - BoardWidth) : 0;
    }

    /// <summary>
    /// Print <paramref name="index"/>'s bottom-left corner on the window with
    /// the row at <paramref name="scroll"/>, physical px: where its menu opens.
    /// Worked out from the layout, as the board may still be turning on its hinge.
    /// </summary>
    public (int X, int Y) MenuPoint(int index, double scroll, double window, double scale)
    {
        var left = (window - BoardWidth) / 2 + BoardWidth / 2 + _x[index] - scroll;
        var bottom = Y + _entries[index].Height;
        return (HalfUp.Px(Math.Max(0, left) * scale), HalfUp.Px(bottom * scale));
    }

    /// <summary>
    /// Where the window takes the pointer for a board <paramref name="width"/>
    /// wide: the board plus <see cref="BoardMetrics.Lift"/> each side, within
    /// the window, from the screen edge to the board's bottom plus the lift.
    /// Physical px relative to the window.
    /// </summary>
    public static Rect BoardRect(double window, double width, double scale)
    {
        var left = Math.Max(0, (window - width) / 2 - BoardMetrics.Lift);
        var right = Math.Min(window, (window + width) / 2 + BoardMetrics.Lift);
        return new Rect(
            HalfUp.Px(left * scale), 0, HalfUp.Px((right - left) * scale), HalfUp.Px((BoardMetrics.BoardHeight + BoardMetrics.Lift) * scale));
    }
}
