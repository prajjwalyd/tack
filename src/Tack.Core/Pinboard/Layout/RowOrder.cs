namespace Tack.Core.Pinboard.Layout;

/// <summary>The row's order as the app sends it, and which of a backlog of prints is the newest.</summary>
public static class RowOrder
{
    /// <summary>
    /// The new order from the app's <paramref name="ids"/>: those the board
    /// knows, each once, in the app's order; then each id of
    /// <paramref name="current"/> the app left out (a print about to leave)
    /// goes back in right after the print it followed, so it keeps its place.
    /// </summary>
    public static List<string> Merge(IReadOnlyList<string> current, IEnumerable<string> ids, Func<string, bool> known)
    {
        var seen = new HashSet<string>();
        var next = new List<string>();
        foreach (var id in ids)
        {
            if (known(id) && seen.Add(id))
            {
                next.Add(id);
            }
        }
        for (var i = 0; i < current.Count; i++)
        {
            if (seen.Add(current[i]))
            {
                next.Insert(i == 0 ? 0 : next.IndexOf(current[i - 1]) + 1, current[i]);
            }
        }
        return next;
    }

    /// <summary>
    /// The index of the newest of a backlog: the latest pinned, then the last
    /// to reach the board; the first of equals. -1 for none.
    /// </summary>
    public static int Newest(IReadOnlyList<(long PinnedAt, long AddedAt)> backlog)
    {
        var newest = -1;
        for (var i = 0; i < backlog.Count; i++)
        {
            if (newest < 0 || backlog[i].CompareTo(backlog[newest]) > 0)
            {
                newest = i;
            }
        }
        return newest;
    }
}
