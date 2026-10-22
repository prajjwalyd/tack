using Tack.Core.Phone.Serving;

namespace Tack.Core.Tests.Phone.Serving;

public class PinLimitsTests
{
    const string Pixel = "cGl4ZWw=";

    [Fact]
    public void A_device_may_pin_thirty_times_in_any_minute()
    {
        var time = new ManualTime();
        var limits = new PinLimits(time);
        for (var i = 0; i < PinLimits.PinsPerMinute; i++)
        {
            Assert.True(limits.MayPin(Pixel));
            time.Advance(TimeSpan.FromSeconds(1));
        }

        // The first pin was 30 s ago: the minute slides, it does not reset.
        Assert.False(limits.MayPin(Pixel));
        time.Advance(TimeSpan.FromMilliseconds(29_900));
        Assert.False(limits.MayPin(Pixel));
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(limits.MayPin(Pixel));
        Assert.False(limits.MayPin(Pixel));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limits.MayPin(Pixel));
    }

    [Fact]
    public void A_refused_pin_is_not_counted()
    {
        var time = new ManualTime();
        var limits = new PinLimits(time);
        for (var i = 0; i < PinLimits.PinsPerMinute; i++)
        {
            limits.MayPin(Pixel);
        }
        for (var i = 0; i < 100; i++)
        {
            Assert.False(limits.MayPin(Pixel));
        }

        time.Advance(TimeSpan.FromMinutes(1));

        for (var i = 0; i < PinLimits.PinsPerMinute; i++)
        {
            Assert.True(limits.MayPin(Pixel));
        }
    }

    [Fact]
    public void Each_key_counts_alone()
    {
        var limits = new PinLimits(new ManualTime());
        for (var i = 0; i < PinLimits.PinsPerMinute; i++)
        {
            limits.MayPin(Pixel);
        }

        Assert.False(limits.MayPin(Pixel));
        Assert.True(limits.MayPin("bGFwdG9w"));
    }

    [Fact]
    public void Two_turns_at_once_and_each_comes_back_once()
    {
        var limits = new PinLimits(new ManualTime());

        var one = limits.TryTurn();
        var two = limits.TryTurn();
        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.Null(limits.TryTurn());

        one.Dispose();
        one.Dispose();
        var three = limits.TryTurn();
        Assert.NotNull(three);
        Assert.Null(limits.TryTurn());

        two.Dispose();
        three.Dispose();
        Assert.NotNull(limits.TryTurn());
        Assert.NotNull(limits.TryTurn());
        Assert.Null(limits.TryTurn());
    }

    [Fact]
    public async Task Turns_hold_under_many_threads()
    {
        var limits = new PinLimits(new ManualTime());
        var held = 0;
        var most = 0;

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                using var turn = limits.TryTurn();
                if (turn is null)
                {
                    continue;
                }
                var now = Interlocked.Increment(ref held);
                InterlockedMax(ref most, now);
                Interlocked.Decrement(ref held);
            }
        }, TestContext.Current.CancellationToken)));

        Assert.InRange(most, 1, PinLimits.MaxPinning);
    }

    static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
