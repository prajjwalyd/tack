using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>Golden values from Tack 0.2's breeze, run as it shipped with the same random numbers.</summary>
public class BreezeTests
{
    const int Precision = 9;

    static Func<double> Sequence(params double[] numbers)
    {
        var i = 0;
        return () => numbers[i++ % numbers.Length];
    }

    static MotionClock Normal()
    {
        var clock = new MotionClock();
        clock.SetScale(0.65);
        return clock;
    }

    [Fact]
    public void A_breeze_ripples_along_the_row_each_print_pushed_a_little_differently()
    {
        var plan = Breeze.Plan(4, Normal(), Sequence(0.7, 0.25, 0.1, 0.9, 0.5, 0.3, 0.6, 0.2, 0.8, 0.4));
        Assert.Equal([0, 1, 2, 3], plan.Select(p => p.Index));
        double[] delays = [1.95, 53.3, 81.25, 113.1];
        double[] amplitudes = [0.531, 0.472, 0.5605, 0.5015];
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(delays[i], plan[i].DelayMs, Precision);
            Assert.Equal(amplitudes[i], plan[i].Amplitude, Precision);
        }
    }

    [Fact]
    public void A_breeze_from_the_right_starts_at_the_last_print_and_pushes_the_other_way()
    {
        var plan = Breeze.Plan(3, new MotionClock(), Sequence(0.2, 0, 0, 0, 0, 1, 1, 1));
        Assert.Equal([2, 1, 0], plan.Select(p => p.Index));
        Assert.Equal([0.0, 55, 110], plan.Select(p => p.DelayMs));
        Assert.All(plan, p => Assert.Equal(-0.8 * 0.65, p.Amplitude, Precision));
    }

    [Fact]
    public void No_prints_in_view_is_no_breeze()
    {
        Assert.Empty(Breeze.Plan(0, new MotionClock(), () => 0.5));
    }

    [Fact]
    public void Breezes_come_every_12_to_25_seconds_at_any_speed()
    {
        Assert.Equal(12000, Breeze.NextMs(() => 0));
        Assert.Equal(25000, Breeze.NextMs(() => 1));
        Assert.Equal(18500, Breeze.NextMs(() => 0.5));
    }
}
