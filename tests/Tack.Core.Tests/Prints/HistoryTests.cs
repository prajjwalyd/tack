using Tack.Core.Prints;

namespace Tack.Core.Tests.Prints;

public class HistoryTests
{
    const long Hour = 60 * 60 * 1000;
    const long Now = 1_800_000_000_000;

    [Fact]
    public void Kept_prints_lead_in_keeping_order_then_the_newest()
    {
        (long? KeptAt, long PinnedAt)[] row = [(null, 10), (500, 1), (null, 30), (200, 2), (null, 20)];

        var sorted = row.OrderBy(e => e, History.RowOrder).ToArray();

        Assert.Equal([(200, 2), (500, 1), (null, 30), (null, 20), (null, 10)], sorted);
    }

    [Fact]
    public void A_week_old_unkept_print_ages_out()
    {
        (long? KeptAt, long PinnedAt)[] entries =
            [(null, Now - History.MaxAgeMs + Hour), (null, Now - History.MaxAgeMs), (Now, Now - 30 * History.MaxAgeMs)];

        // Just under a week stays; a week exactly goes; kept never goes.
        Assert.Equal([false, true, false], History.AgedOut(entries, Now));
    }

    [Fact]
    public void The_history_holds_fifty_unkept_prints()
    {
        // Oldest first, as an old board.json lists them, with kept ones mixed in.
        var entries = Enumerable.Range(0, History.MaxUnkept + 2).Select(n => ((long?)null, Now - 100 + n)).ToList();
        entries.Insert(0, (Now, Now - 1000));
        entries.Add((Now, Now - 2000));

        var gone = History.AgedOut(entries, Now);

        // The two oldest unkept go; kept ones are not counted.
        Assert.Equal(2, gone.Count(g => g));
        Assert.True(gone[1] && gone[2]);
        Assert.False(gone[0] || gone[^1]);
    }

    [Fact]
    public void Ties_count_the_earlier_entry_as_newer()
    {
        var entries = Enumerable.Repeat(((long?)null, Now), History.MaxUnkept + 1).ToList();

        var gone = History.AgedOut(entries, Now);

        Assert.All(gone.Take(History.MaxUnkept), g => Assert.False(g));
        Assert.True(gone[History.MaxUnkept]);
    }

    [Fact]
    public void A_clock_that_went_back_ages_nothing() =>
        Assert.Equal([false], History.AgedOut([(null, Now + Hour)], Now));
}
