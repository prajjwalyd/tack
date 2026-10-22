using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Motion;
using Tack.Core.Reveal;

namespace Tack.Core.Tests.Pinboard.Motion;

public class MotionClockTests
{
    [Fact]
    public void Slow_plays_everything_as_written_and_the_others_scale_it()
    {
        var clock = new MotionClock();
        Assert.Equal(330, clock.Scaled(330));
        Assert.Equal(1000, clock.ScaledMin(1000, 600));
        Assert.True(clock.SetScale(0.5));
        Assert.Equal(165, clock.Scaled(330));
        Assert.Equal(600, clock.ScaledMin(1000, 600));
        Assert.Equal(500, clock.ScaledMin(1000, 300));
        // A floor never makes it longer than Slow.
        Assert.Equal(100, clock.ScaledMin(100, 600));
        Assert.Equal(520, MotionClock.Fixed(520, "a physical gesture time"));
    }

    [Fact]
    public void A_bad_scale_is_ignored()
    {
        var clock = new MotionClock();
        clock.SetScale(0.65);
        foreach (var bad in new[] { 0, -1, 1.5, double.NaN, double.PositiveInfinity })
        {
            Assert.False(clock.SetScale(bad));
        }
        Assert.Equal(0.65, clock.Scale);
        Assert.False(clock.SetScale(0.65));
    }

    [Fact]
    public void The_board_rests_after_the_swing_plus_two_frames_or_a_short_slide_when_reduced()
    {
        var clock = new MotionClock();
        Assert.Equal(517, clock.RestMs(reduced: false));
        Assert.Equal(100, clock.RestMs(reduced: true));
        clock.SetScale(RevealSpeed.Normal.MotionScale());
        Assert.Equal(477 * 0.65 + 40, clock.RestMs(reduced: false), 9);
        Assert.Equal(65, clock.RestMs(reduced: true), 9);
        Assert.Equal(452 * 0.65, clock.LayoutMs, 9);
        Assert.Equal(190 * 0.65 + 30, clock.TuckCleanupMs, 9);
    }

    [Fact]
    public void The_tuck_is_one_length_on_the_board_and_in_the_app()
    {
        Assert.Equal(RevealSpeedTimes.TuckMs, BoardMetrics.TuckMs);
        // The window hides only after the board's own clean-up has run.
        Assert.True(BoardMetrics.TuckCleanupMarginMs < RevealSpeedTimes.TuckMargin.TotalMilliseconds);
    }
}
