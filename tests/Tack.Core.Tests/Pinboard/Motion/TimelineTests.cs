using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

public class TimelineTests
{
    const int Precision = 9;

    /// <summary>The pin-on drop: a held pose, eased down to rest.</summary>
    static SampledMotion PinOnDrop() => new Timeline(330)
        .Key(0, new Pose { Opacity = 0, Y = -7, Scale = 1.06, Rotation = 1.2 })
        .Key(0.2, new Pose { Opacity = 1, Y = -6 }, new CubicBezier(0.45, 0, 0.25, 1))
        .Key(1, Pose.None)
        .Sample();

    [Fact]
    public void Each_channel_follows_its_keyframes_and_the_easing_of_the_one_it_leaves()
    {
        var drop = PinOnDrop();
        Assert.Equal([Channel.Opacity, Channel.Y, Channel.Scale, Channel.Rotation], drop.Channels);
        Assert.Equal(0, drop.ValueAt(Channel.Opacity, 0));
        Assert.Equal(-7, drop.ValueAt(Channel.Y, 0));
        Assert.Equal(0.5, drop.ValueAt(Channel.Opacity, 0.1), Precision);
        Assert.Equal(-6.5, drop.ValueAt(Channel.Y, 0.1), Precision);
        var eased = new CubicBezier(0.45, 0, 0.25, 1).At(0.5);
        Assert.Equal(-6 + 6 * eased, drop.ValueAt(Channel.Y, 0.6), Precision);
        Assert.Equal(0, drop.ValueAt(Channel.Y, 1));
    }

    [Fact]
    public void A_keyframe_that_moves_the_print_sets_its_whole_transform()
    {
        // The keyframe at .2 names only Y, so scale and rotation are back at rest there.
        var drop = PinOnDrop();
        Assert.Equal(1, drop.ValueAt(Channel.Scale, 0.2), Precision);
        Assert.Equal(0, drop.ValueAt(Channel.Rotation, 0.2), Precision);
        Assert.Equal(1.03, drop.ValueAt(Channel.Scale, 0.1), Precision);
    }

    [Fact]
    public void A_channel_a_keyframe_leaves_out_has_no_keyframe_there()
    {
        var motion = new Timeline(100)
            .Key(0, new Pose { Opacity = 0, Y = -5 })
            .Key(0.5, new Pose { Opacity = 1 })
            .Key(1, new Pose { Y = 0 })
            .Sample();
        Assert.Equal(-2.5, motion.ValueAt(Channel.Y, 0.5), Precision);
        // Opacity has no keyframe at the end, so it ends at its resting value.
        Assert.Equal(1, motion.ValueAt(Channel.Opacity, 0.75), Precision);
    }

    [Fact]
    public void A_missing_end_keyframe_falls_back_to_the_underlying_value()
    {
        var motion = new Timeline(100).Underlying(Channel.Opacity, 0.5).Key(0.5, new Pose { Opacity = 1 }).Sample();
        Assert.Equal(0.5, motion.ValueAt(Channel.Opacity, 0));
        Assert.Equal(1, motion.ValueAt(Channel.Opacity, 0.5), Precision);
        Assert.Equal(0.5, motion.ValueAt(Channel.Opacity, 1));
        Assert.Equal(0.5, motion.Underlying(Channel.Opacity));
    }

    [Fact]
    public void Offsets_left_out_are_spread_evenly()
    {
        var motion = new Timeline(100)
            .Key(null, new Pose { X = 0 })
            .Key(null, new Pose { X = 10 })
            .Key(null, new Pose { X = 40 })
            .Sample();
        Assert.Equal(5, motion.ValueAt(Channel.X, 0.25), Precision);
        Assert.Equal(25, motion.ValueAt(Channel.X, 0.75), Precision);
    }

    [Fact]
    public void Offsets_out_of_order_or_range_are_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            new Timeline(100).Key(0.5, new Pose { X = 1 }).Key(0.2, new Pose { X = 2 }).Sample());
        Assert.Throws<ArgumentException>(() => new Timeline(100).Key(1.5, new Pose { X = 1 }).Sample());
        Assert.Throws<ArgumentException>(() => new Timeline(100).Sample());
    }

    [Fact]
    public void The_global_easing_bends_time_and_a_keyframe_still_lands_exactly()
    {
        // The pin-on's pin: a global easing, an overshooting keyframe easing, a delay held backwards.
        var pin = new Timeline(170, delay: 250, easing: new CubicBezier(0.5, 0, 0.9, 0.4), fill: Fill.Backwards)
            .Key(null, new Pose { Opacity = 0, Y = -4, Scale = 1.7 })
            .Key(0.6, new Pose { Opacity = 1, Scale = 0.9 }, new CubicBezier(0.3, 1.4, 0.5, 1))
            .Key(null, Pose.None with { Opacity = 1 })
            .Sample();
        Assert.Equal((170.0, 250.0, Fill.Backwards), (pin.Duration, pin.Delay, pin.Fill));
        Assert.Equal((-4.0, 1.7), (pin.ValueAt(Channel.Y, 0), pin.ValueAt(Channel.Scale, 0)));
        Assert.Equal((0.0, 1.0, 1.0), (pin.ValueAt(Channel.Y, 1), pin.ValueAt(Channel.Scale, 1), pin.ValueAt(Channel.Opacity, 1)));
        var landed = Enumerable.Range(0, pin.Times.Count).Single(i => Math.Abs(pin[Channel.Scale][i] - 0.9) < 1e-9);
        Assert.Equal(0, pin[Channel.Y][landed], Precision);
        Assert.Equal(1, pin[Channel.Opacity][landed], Precision);
        Assert.True(pin[Channel.Scale].Max() > 1, "the overshooting segment goes past rest");
    }

    [Fact]
    public void A_spring_as_the_global_easing_is_reproduced_at_its_own_samples()
    {
        var swing = new Timeline(477, easing: Spring.Reveal.Easing)
            .Key(0, new Pose { Rotation = -88 })
            .Key(1, new Pose { Rotation = 0 })
            .Sample();
        for (var i = 0; i <= Spring.Steps; i++)
        {
            var t = i / (double)Spring.Steps;
            Assert.Contains(swing.Times, at => Math.Abs(at - t) < 1e-12);
            Assert.Equal(-88 * (1 - Spring.Reveal.Samples[i]), swing.ValueAt(Channel.Rotation, t), Precision);
        }
    }

    [Fact]
    public void Samples_are_dense_ordered_and_span_the_whole_motion()
    {
        var motion = new Timeline(1000).Key(0, new Pose { X = 0 }).Key(1, new Pose { X = 1 }).Sample();
        Assert.True(motion.Times.Count >= 121, $"{motion.Times.Count} samples");
        Assert.Equal(0, motion.Times[0]);
        Assert.Equal(1, motion.Times[^1]);
        for (var i = 1; i < motion.Times.Count; i++)
        {
            Assert.True(motion.Times[i] > motion.Times[i - 1]);
        }
        Assert.Equal(motion.Times.Count, motion[Channel.X].Count);
    }

    [Fact]
    public void Opacity_never_leaves_0_to_1_however_the_easing_overshoots()
    {
        var motion = new Timeline(100, easing: new CubicBezier(0.3, 1.4, 0.5, 1))
            .Key(0, new Pose { Opacity = 0, Scale = 0 })
            .Key(1, new Pose { Opacity = 1, Scale = 1 })
            .Sample();
        Assert.True(motion[Channel.Opacity].Max() <= 1);
        Assert.True(motion[Channel.Scale].Max() > 1);
    }
}
