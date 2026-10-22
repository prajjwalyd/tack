using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>Golden values from Tack 0.2's swing, run as it shipped (its frames were written to three decimals).</summary>
public class SwingTests
{
    const double Frame = 0.0005;

    /// <summary>The settle after a pin-on on a print tilted right: a nudge of -0.6 from rest, at Slow.</summary>
    static readonly double[] SettleFrames =
    [
        0, -0.113, -0.211, -0.293, -0.358, -0.407, -0.439, -0.455, -0.458, -0.448, -0.426, -0.395, -0.357, -0.313,
        -0.265, -0.215, -0.165, -0.115, -0.068, -0.023, 0.017, 0.052, 0.082, 0.107, 0.126, 0.14, 0.148, 0.152, 0.151,
        0.146, 0.137, 0.126, 0.112, 0.097, 0.081, 0.064, 0.047, 0.031, 0.016, 0.002, -0.011, -0.022, -0.031, -0.038,
        -0.044, -0.048, -0.05, -0.05, -0.049, -0.047, -0.044, -0.04, -0.035, -0.03, -0.024, -0.019, -0.013, 0,
    ];

    /// <summary>A copy's nudge of -0.6 landing 400 ms into a nudge of 1.2, at Normal.</summary>
    static readonly double[] BlendedFrames =
    [
        0.088, -0.108, -0.28, -0.427, -0.546, -0.639, -0.704, -0.744, -0.759, -0.753, -0.727, -0.684, -0.628, -0.56,
        -0.485, -0.405, -0.322, -0.24, -0.16, -0.084, -0.013, 0.05, 0.105, 0.151, 0.189, 0.217, 0.237, 0.248, 0.252,
        0.248, 0.238, 0.223, 0.203, 0.18, 0.155, 0.128, 0.1, 0.073, 0.047, 0.022, -0.001, -0.021, -0.039, -0.053,
        -0.065, -0.074, -0.08, -0.083, -0.083, -0.081, -0.078, -0.072, -0.066, -0.058, -0.049, -0.04, -0.031, -0.022,
        -0.014, -0.005, 0.002, 0.008, 0.014, 0.019, 0.022, 0.025, 0.027, 0,
    ];

    [Fact]
    public void A_nudge_from_rest_swings_and_dies_away_in_its_own_time()
    {
        var swing = Swing.Nudge(-0.6, scale: 1);
        Assert.Equal(0, swing.C1);
        Assert.Equal(-0.75, swing.C2, 12);
        Assert.Equal(1.8934563675695297, swing.Duration, 12);
        Assert.Equal(1893.4563675695297, swing.WallMs, 9);
        Assert.Equal(SettleFrames.Length, swing.Frames.Count);
        for (var i = 0; i < SettleFrames.Length; i++)
        {
            Assert.Equal(SettleFrames[i], swing.Frames[i], Frame);
        }
    }

    [Fact]
    public void A_faster_speed_plays_the_same_swing_quicker()
    {
        var slow = Swing.Nudge(-0.6, scale: 1);
        var instant = Swing.Nudge(-0.6, scale: 0.35);
        Assert.Equal(662.7097286493354, instant.WallMs, 9);
        Assert.Equal(slow.Frames, instant.Frames);
    }

    [Fact]
    public void A_copy_swings_further_and_for_longer()
    {
        var swing = Swing.Nudge(1.2, scale: 1);
        Assert.Equal(1.5, swing.C2, 12);
        Assert.Equal(2.3011900031930272, swing.Duration, 12);
        Assert.Equal(71, swing.Frames.Count);
        Assert.Equal(0.916, swing.Frames[8], Frame);
        Assert.Equal(-0.303, swing.Frames[27], Frame);
        Assert.Equal(0, swing.Frames[^1]);
    }

    [Fact]
    public void A_nudge_on_a_swing_adds_to_where_it_is_and_how_fast_it_moves()
    {
        var first = Swing.Nudge(1.2, scale: 0.65);
        var (theta, velocity) = first.AtWall(400);
        Assert.Equal(0.08774657568003451, theta, 12);
        Assert.Equal(-2.6603749145270488, velocity, 12);
        var blended = Swing.Nudge(-0.6, scale: 0.65, first, elapsedMs: 400);
        Assert.Equal(0.08774657568003451, blended.C1, 12);
        Assert.Equal(-1.2695720477799652, blended.C2, 12);
        Assert.Equal(2.2030810416183844, blended.Duration, 12);
        Assert.Equal(1432.00267705195, blended.WallMs, 9);
        Assert.Equal(BlendedFrames.Length, blended.Frames.Count);
        for (var i = 0; i < BlendedFrames.Length; i++)
        {
            Assert.Equal(BlendedFrames[i], blended.Frames[i], Frame);
        }
    }

    [Fact]
    public void A_swing_that_has_died_away_is_at_rest()
    {
        var swing = Swing.Nudge(1.2, scale: 1);
        Assert.Equal((0.0, 0.0), swing.AtWall(swing.WallMs + 1));
    }

    [Fact]
    public void A_swing_lasts_between_0_6_and_3_5_seconds()
    {
        Assert.Equal(0.6, Swing.Nudge(0.001, scale: 1).Duration);
        Assert.Equal(3.5, Swing.Nudge(40, scale: 1).Duration);
    }

    [Fact]
    public void Its_timeline_turns_the_layer_straight_between_frames_over_its_wall_time()
    {
        var swing = Swing.Nudge(-0.6, scale: 0.65);
        var timeline = swing.Timeline();
        Assert.Equal(swing.WallMs, timeline.Duration);
        var motion = timeline.Sample();
        Assert.Equal([Channel.Rotation], motion.Channels);
        Assert.Equal(SettleFrames[8], motion.ValueAt(Channel.Rotation, 8.0 / 57), Frame);
        Assert.Equal(0, motion.ValueAt(Channel.Rotation, 1));
    }
}
