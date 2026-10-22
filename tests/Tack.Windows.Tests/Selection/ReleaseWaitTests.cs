using Tack.Windows.Selection;

namespace Tack.Windows.Tests.Selection;

public sealed class ReleaseWaitTests
{
    static readonly TimeSpan T0 = TimeSpan.FromHours(1);

    static TimeSpan Ms(int n) => TimeSpan.FromMilliseconds(n);

    [Fact]
    public void Keys_already_up_are_ready_after_a_short_settle()
    {
        var wait = new ReleaseWait(T0);
        Assert.Equal(Release.Wait, wait.Step(false, T0));
        Assert.Equal(Release.Wait, wait.Step(false, T0 + Ms(10)));
        Assert.Equal(Release.Ready, wait.Step(false, T0 + ReleaseWait.Settle));
    }

    [Fact]
    public void A_key_pressed_again_restarts_the_settle()
    {
        var wait = new ReleaseWait(T0);
        Assert.Equal(Release.Wait, wait.Step(true, T0));
        Assert.Equal(Release.Wait, wait.Step(false, T0 + Ms(100)));
        // Alt bounced.
        Assert.Equal(Release.Wait, wait.Step(true, T0 + Ms(120)));
        Assert.Equal(Release.Wait, wait.Step(false, T0 + Ms(130)));
        Assert.Equal(Release.Ready, wait.Step(false, T0 + Ms(130) + ReleaseWait.Settle));
    }

    [Fact]
    public void Keys_held_too_long_give_up()
    {
        var wait = new ReleaseWait(T0);
        Assert.Equal(Release.Wait, wait.Step(true, T0 + ReleaseWait.Patience - Ms(1)));
        Assert.Equal(Release.TimedOut, wait.Step(true, T0 + ReleaseWait.Patience));
        // Released in time, it never times out, however long it settles.
        var late = new ReleaseWait(T0);
        Assert.Equal(Release.Wait, late.Step(false, T0 + ReleaseWait.Patience * 2));
    }
}
