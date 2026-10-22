using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>The print motions' keyframes, as Tack 0.2 played them.</summary>
public class BoardMotionsTests
{
    const int Precision = 9;

    static readonly MotionClock Slow = new();

    [Fact]
    public void A_pin_on_holds_the_print_above_the_cork_then_presses_it_down()
    {
        var drop = BoardMotions.PinOnDrop(Slow, 1.2).Sample();
        Assert.Equal(330, drop.Duration);
        Assert.Equal((0.0, -7.0, 1.06, 1.2), (drop.ValueAt(Channel.Opacity, 0), drop.ValueAt(Channel.Y, 0), drop.ValueAt(Channel.Scale, 0), drop.ValueAt(Channel.Rotation, 0)));
        Assert.Equal(-6, drop.ValueAt(Channel.Y, 0.2), Precision);
        Assert.Equal(1.06, drop.ValueAt(Channel.Scale, 0.2), Precision);
        Assert.Equal(-6 + 6 * BoardMotions.Press.At(0.5), drop.ValueAt(Channel.Y, 0.6), Precision);
        Assert.Equal((1.0, 0.0, 1.0, 0.0), (drop.ValueAt(Channel.Opacity, 1), drop.ValueAt(Channel.Y, 1), drop.ValueAt(Channel.Scale, 1), drop.ValueAt(Channel.Rotation, 1)));
    }

    [Fact]
    public void The_pin_drives_in_after_its_delay_showing_its_first_frame_meanwhile()
    {
        var pin = BoardMotions.PinDrop(Slow, 250);
        Assert.Equal((170.0, 250.0, Fill.Backwards), (pin.Duration, pin.Delay, pin.Fill));
        var motion = pin.Sample();
        Assert.Equal((0.0, -4.0, 1.7), (motion.ValueAt(Channel.Opacity, 0), motion.ValueAt(Channel.Y, 0), motion.ValueAt(Channel.Scale, 0)));
        // The global easing reaches the squash at .6 later than .6 of the time.
        var squash = new CubicBezier(0.5, 0, 0.9, 0.4);
        Assert.True(squash.At(0.6) < 0.6);
        Assert.Equal((1.0, 0.0, 1.0), (motion.ValueAt(Channel.Opacity, 1), motion.ValueAt(Channel.Y, 1), motion.ValueAt(Channel.Scale, 1)));
    }

    [Fact]
    public void The_dimple_eases_each_step_and_settles_at_the_shadow_s_resting_depth()
    {
        var dimple = BoardMotions.Dimple(Slow, 0.9);
        Assert.Equal((300.0, 330.0, Fill.Backwards), (dimple.Duration, dimple.Delay, dimple.Fill));
        var motion = dimple.Sample();
        var ease = new CubicBezier(0.2, 0.7, 0.3, 1);
        Assert.Equal(0.3 + 0.7 * ease.At(0.5), motion.ValueAt(Channel.Opacity, 0.225), 3);
        Assert.Equal(1.12, motion.ValueAt(Channel.Scale, 0.45), Precision);
        Assert.Equal(0.9, motion.ValueAt(Channel.Opacity, 1), Precision);
        Assert.Equal(0.9, motion.Underlying(Channel.Opacity));
    }

    [Fact]
    public void The_keep_press_squeezes_the_pin_and_springs_it_back_proud()
    {
        var pin = BoardMotions.KeepPin(Slow).Sample();
        Assert.Equal([Channel.Scale], pin.Channels);
        Assert.Equal(1, pin.ValueAt(Channel.Scale, 1));
        Assert.True(pin.Channels.Count == 1 && pin[Channel.Scale].Min() < 0.75 && pin[Channel.Scale].Max() > 1.05);
        var drop = BoardMotions.KeepDrop(Slow);
        Assert.Equal(320, drop.Duration);
    }

    [Fact]
    public void The_glint_crosses_the_brass_and_rests_hidden_to_its_left()
    {
        var glint = BoardMotions.Glint(Slow, 18).Sample();
        Assert.Equal((-18.0, 1.0), (glint.ValueAt(Channel.X, 0), glint.ValueAt(Channel.Opacity, 0)));
        Assert.Equal(18, glint.ValueAt(Channel.X, 1));
        Assert.Equal((-18.0, 0.0), (glint.Underlying(Channel.X), glint.Underlying(Channel.Opacity)));
    }

    [Fact]
    public void A_fall_pops_the_pin_and_tumbles_the_print_into_the_overhang()
    {
        var pin = LeaveMotions.FallPin(Slow, -1).Sample();
        // Its global easing reaches the keyframe at .4 early; the keyframe's values land exactly on a frame.
        var popped = pin.Times.Select((_, i) => i).Single(i => Math.Abs(pin[Channel.Y][i] + 5) < 1e-9);
        Assert.True(pin.Times[popped] < 0.4);
        Assert.Equal((-1.0, 1.12), (pin[Channel.X][popped], pin[Channel.Scale][popped]));
        Assert.Equal((-3.0, -9.0, 0.0), (pin.ValueAt(Channel.X, 1), pin.ValueAt(Channel.Y, 1), pin.ValueAt(Channel.Opacity, 1)));
        var drop = LeaveMotions.FallDrop(Slow, 1, 8);
        Assert.Equal((450.0, 50.0, Fill.Forwards), (drop.Duration, drop.Delay, drop.Fill));
        var motion = drop.Sample();
        Assert.Equal(0.85, motion.ValueAt(Channel.Opacity, 0.5), Precision);
        // The opacity-only keyframe at .5 leaves the transform alone: it eases from .12 to the end.
        var eased = new CubicBezier(0.5, 0, 0.9, 0.55).At((0.5 - 0.12) / 0.88);
        Assert.Equal(-2 + 66 * eased, motion.ValueAt(Channel.Y, 0.5), Precision);
        Assert.Equal((6.0, 64.0, 8.0, 0.0), (motion.ValueAt(Channel.X, 1), motion.ValueAt(Channel.Y, 1), motion.ValueAt(Channel.Rotation, 1), motion.ValueAt(Channel.Opacity, 1)));
        Assert.Equal(500, LeaveMotions.LongestMs(Slow));
    }

    [Fact]
    public void The_copied_check_stamps_on_and_goes_away_smaller()
    {
        var badge = BoardMotions.BadgeIn(Slow, reduced: false).Sample();
        Assert.Equal((0.0, 1.4), (badge.ValueAt(Channel.Opacity, 0), badge.ValueAt(Channel.Scale, 0)));
        Assert.Equal((1.0, 1.0), (badge.ValueAt(Channel.Opacity, 1), badge.ValueAt(Channel.Scale, 1)));
        Assert.Equal(1, BoardMotions.BadgeIn(Slow, reduced: true).Duration);
        var gone = BoardMotions.BadgeOut(Slow).Sample();
        Assert.Equal((0.0, 0.92, Fill.Forwards), (gone.ValueAt(Channel.Opacity, 1), gone.ValueAt(Channel.Scale, 1), gone.Fill));
    }

    [Fact]
    public void The_tip_swings_in_and_settles_a_little_crooked()
    {
        var tip = TipMotions.In(Slow).Sample();
        Assert.Equal((0.0, -10.0, -2.4), (tip.ValueAt(Channel.Opacity, 0), tip.ValueAt(Channel.Y, 0), tip.ValueAt(Channel.Rotation, 0)));
        Assert.Equal((1.0, 0.0, -0.6), (tip.ValueAt(Channel.Opacity, 1), tip.ValueAt(Channel.Y, 1), tip.ValueAt(Channel.Rotation, 1)));
        Assert.Equal(-0.6, tip.Underlying(Channel.Rotation));
        Assert.Equal([Channel.Opacity], TipMotions.Out(Slow, reduced: true).Sample().Channels);
    }
}
