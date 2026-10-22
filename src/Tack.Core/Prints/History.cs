namespace Tack.Core.Prints;

/// <summary>
/// The rules that shape the row of prints. Kept prints come first, in the order
/// they were kept; then the rest, newest first, so scrolling right goes back
/// in time. That history is short-term memory: an unkept print leaves once it
/// is a week old, or once fifty newer unkept prints have arrived. Times are
/// wall-clock ms since the Unix epoch, passed in, so every rule is testable.
/// </summary>
public static class History
{
    /// <summary>How long an unkept print stays: seven days, in ms.</summary>
    public const long MaxAgeMs = 7L * 24 * 60 * 60 * 1000;

    /// <summary>How many unkept prints the history holds; one more ages out the oldest.</summary>
    public const int MaxUnkept = 50;

    /// <summary>
    /// Where a print sorts in the row, from (kept at, pinned at). Use it with a
    /// stable sort, so prints that tie keep their places.
    /// </summary>
    public static IComparer<(long? KeptAt, long PinnedAt)> RowOrder { get; } =
        Comparer<(long? KeptAt, long PinnedAt)>.Create(static (a, b) => (a.KeptAt, b.KeptAt) switch
        {
            ({ } x, { } y) => x.CompareTo(y),
            ({ }, null) => -1,
            (null, { }) => 1,
            _ => b.PinnedAt.CompareTo(a.PinnedAt),
        });

    /// <summary>The wall clock, in ms since the Unix epoch.</summary>
    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Which entries have aged out at <paramref name="nowMs"/>, one flag per entry.
    /// An unkept entry ages out when it was pinned <see cref="MaxAgeMs"/> or more
    /// ago, or when <see cref="MaxUnkept"/> newer unkept entries are ahead of it.
    /// Among unkept entries pinned at the same moment, the earlier one counts as newer.
    /// </summary>
    public static bool[] AgedOut(IReadOnlyList<(long? KeptAt, long PinnedAt)> entries, long nowMs)
    {
        var gone = new bool[entries.Count];
        var unkept = Enumerable.Range(0, entries.Count)
            .Where(i => entries[i].KeptAt is null)
            .OrderByDescending(i => entries[i].PinnedAt);
        var rank = 0;
        foreach (var i in unkept)
        {
            var age = Math.Max(nowMs - entries[i].PinnedAt, 0);
            gone[i] = rank >= MaxUnkept || age >= MaxAgeMs;
            rank++;
        }
        return gone;
    }

    /// <summary>
    /// Takes the items that have aged out out of <paramref name="items"/> and
    /// hands them back, both lists keeping their order.
    /// </summary>
    public static List<T> TakeAged<T>(List<T> items, Func<T, (long? KeptAt, long PinnedAt)> times, long nowMs)
    {
        var gone = AgedOut([.. items.Select(times)], nowMs);
        var aged = new List<T>();
        if (!gone.Contains(true))
        {
            return aged;
        }
        var stay = new List<T>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            (gone[i] ? aged : stay).Add(items[i]);
        }
        items.Clear();
        items.AddRange(stay);
        return aged;
    }
}
