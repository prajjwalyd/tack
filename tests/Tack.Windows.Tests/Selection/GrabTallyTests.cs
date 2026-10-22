using Tack.Windows.Selection;

namespace Tack.Windows.Tests.Selection;

public sealed class GrabTallyTests
{
    static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task A_grab_still_counts_for_its_tail()
    {
        var tally = new GrabTally(Tail);
        Assert.False(tally.Any);

        tally.Begin();
        tally.EndAfterTail();
        Assert.True(tally.Any, "a last clipboard event may still be on its way");
        await WhenQuiet(tally);
    }

    [Fact]
    public async Task An_earlier_tail_never_ends_a_later_grab()
    {
        var tally = new GrabTally(Tail);
        tally.Begin();
        tally.EndAfterTail();
        tally.Begin();

        await Task.Delay(Tail * 3, TestContext.Current.CancellationToken);
        Assert.True(tally.Any);
        tally.EndAfterTail();
        await WhenQuiet(tally);
    }

    static async Task WhenQuiet(GrabTally tally)
    {
        var giveUp = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (tally.Any && DateTime.UtcNow < giveUp)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.False(tally.Any);
    }
}
