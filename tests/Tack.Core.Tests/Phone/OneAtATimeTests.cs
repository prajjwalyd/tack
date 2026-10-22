using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

public class OneAtATimeTests
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Nudges_during_a_run_make_it_run_once_more()
    {
        var runs = 0;
        var started = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        var work = new OneAtATime("test", async () =>
        {
            Interlocked.Increment(ref runs);
            started.Release();
            await release.WaitAsync(TestContext.Current.CancellationToken);
        });

        work.Nudge();
        Assert.True(await started.WaitAsync(Patience, TestContext.Current.CancellationToken));
        work.Nudge();
        work.Nudge();
        work.Nudge();
        release.Release();

        Assert.True(await started.WaitAsync(Patience, TestContext.Current.CancellationToken));
        release.Release();
        // No third run follows.
        Assert.False(await started.WaitAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
        Assert.Equal(2, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task A_failed_run_does_not_stop_the_next()
    {
        var runs = new SemaphoreSlim(0);
        var work = new OneAtATime("test", () =>
        {
            runs.Release();
            throw new InvalidOperationException("broken");
        });

        work.Nudge();
        Assert.True(await runs.WaitAsync(Patience, TestContext.Current.CancellationToken));
        // Whether the failed run is still finishing or over, this nudge runs it again.
        work.Nudge();
        Assert.True(await runs.WaitAsync(Patience, TestContext.Current.CancellationToken));
    }
}
