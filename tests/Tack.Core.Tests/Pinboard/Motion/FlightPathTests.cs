using Tack.Core.Geometry;
using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>
/// Golden frames from Tack 0.2's flight, run as it shipped in a 1920 px wide
/// window (it wrote translations to 2 decimals, turns to 3, scales to 4).
/// </summary>
public class FlightPathTests
{
    static MotionClock Clock(double scale)
    {
        var clock = new MotionClock();
        clock.SetScale(scale);
        return clock;
    }

    /// <summary>A large snip on the left flying up to a slot right of the middle, at Slow.</summary>
    static FlightPath Found() => new(new LogicalRect(300, 500, 800, 450), new LogicalRect(900, 13, 168, 98), 1.1, 1.3, true, 1920, Clock(1));

    static void Same(double[] expected, FlightFrame actual)
    {
        Assert.Equal(expected[0], actual.Offset, 8);
        Assert.Equal(expected[1], actual.X, 0.005);
        Assert.Equal(expected[2], actual.Y, 0.005);
        Assert.Equal(expected[3], actual.Rotation, 0.0005);
        Assert.Equal(expected[4], actual.ScaleX, 0.00005);
        Assert.Equal(expected[5], actual.ScaleY, 0.00005);
        Assert.Equal(expected[6], actual.Opacity, 9);
    }

    static void Golden(FlightPath path, Dictionary<int, double[]> frames)
    {
        Assert.Equal(FlightPath.FrameCount + 2, path.Frames.Count);
        foreach (var (index, expected) in frames)
        {
            Same(expected, path.Frames[index]);
        }
    }

    [Fact]
    public void A_found_snip_lifts_off_where_it_was_arcs_to_its_slot_and_is_pressed_down()
    {
        Golden(Found(), new()
        {
            [0] = [0, -284, 495, 0, 5, 5, 1],
            [1] = [0.013888889, -284, 493.42, 0, 5.0376, 5.0376, 1],
            [5] = [0.069444444, -284, 490.19, 0, 5.1145, 5.1145, 1],
            [8] = [0.111111111, -284, 489.76, 0, 5.1249, 5.1249, 1],
            [20] = [0.277777778, -237.92, 490.96, 0, 4.0993, 4.0993, 1],
            [36] = [0.5, -33.43, 143.68, 2.133, 1.2179, 1.2179, 1],
            [52] = [0.722222222, -0.3, -4.44, 2.4, 1.06, 1.06, 1],
            [60] = [0.833333333, -0.01, -3.96, 1.958, 1.0396, 1.0396, 1],
            [66] = [0.916666667, 0, -0.57, 1.224, 1.0057, 1.0057, 1],
            [70] = [0.972222222, 0, -0.05, 1.111, 1.0005, 1.0005, 1],
            [72] = [0.999438202, 0, 0, 1.1, 1, 1, 1],
            [73] = [1, 0, 0, 1.1, 1, 1, 0],
        });
    }

    [Fact]
    public void A_snip_not_found_grows_out_of_the_pointer()
    {
        var path = new FlightPath(new LogicalRect(1500, 700, 160, 90), new LogicalRect(700, 13, 168, 98), -0.8, -1.2, false, 1920, Clock(0.65));
        Golden(path, new()
        {
            [0] = [0, 796, 701.9, 0, 0.55, 0.55, 0],
            [1] = [0.013888889, 796, 698.69, 0, 0.6263, 0.6263, 0.1592670933557756],
            [5] = [0.069444444, 796, 689.23, 0, 0.8518, 0.8518, 0.6282032060498394],
            [8] = [0.111111111, 796, 685.18, 0, 0.948, 0.948, 0.8331662456624194],
            [20] = [0.277777778, 655.54, 638.62, 0, 1.0299, 1.0299, 1],
            [36] = [0.5, 82.08, 155.13, -1.777, 1.0569, 1.0569, 1],
            [52] = [0.722222222, 0.7, -4.39, -2, 1.06, 1.06, 1],
            [66] = [0.916666667, 0, -0.57, -0.915, 1.0057, 1.0057, 1],
            [72] = [0.999135696, 0, 0, -0.8, 1, 1, 1],
            [73] = [1, 0, 0, -0.8, 1, 1, 0],
        });
    }

    [Fact]
    public void A_snip_nearly_straight_below_bows_away_from_the_window_middle()
    {
        var path = new FlightPath(new LogicalRect(880, 600, 400, 300), new LogicalRect(1000, 13, 141, 108), 0.5, 1, true, 1920, Clock(0.35));
        Golden(path, new()
        {
            [0] = [0, 9.5, 589, 0, 3.0075, 3, 1],
            [8] = [0.111111111, 9.5, 585.48, 0, 3.0826, 3.0749, 1],
            [20] = [0.277777778, 28.76, 562.62, 0, 2.6499, 2.6442, 1],
            [36] = [0.5, 25.49, 150.03, 1.333, 1.1646, 1.1643, 1],
            [60] = [0.833333333, -0.01, -3.96, 1.16, 1.0396, 1.0396, 1],
            [72] = [0.998394864, 0, 0, 0.5, 1, 1, 1],
        });
    }

    [Fact]
    public void Its_times_follow_the_speed()
    {
        var slow = Found().Times;
        Assert.Equal((110.0, 560.0, 220.0, 890.0), (slow.Lift, slow.Travel, slow.Press, slow.Total));
        Assert.Equal((790.0, 885.0, 950.0, 380.0), (slow.PinAt, slow.TockAt, slow.SettledAt, slow.TipDelay));
        var normal = FlightTimes.At(Clock(0.65));
        Assert.Equal(578.5, normal.Total, 9);
        Assert.Equal(110.5, normal.Pin, 9);
        Assert.Equal(143, normal.Grow, 9);
        Assert.Equal(39, normal.HandOver, 9);
    }

    [Fact]
    public void The_stand_in_plays_its_frames_and_hides_on_the_last()
    {
        var path = Found();
        var motion = path.StandIn().Sample();
        Assert.Equal(890, motion.Duration);
        Assert.Equal(Fill.Forwards, motion.Fill);
        Assert.Equal([Channel.Opacity, Channel.X, Channel.Y, Channel.ScaleX, Channel.ScaleY, Channel.Rotation], motion.Channels);
        Assert.Equal(-33.43, motion.ValueAt(Channel.X, 0.5), 0.005);
        Assert.Equal(1, motion.ValueAt(Channel.Opacity, 0.999));
        Assert.Equal(0, motion.ValueAt(Channel.Opacity, 1));
    }

    [Fact]
    public void Its_shade_paper_and_pin_are_timed_to_the_phases()
    {
        var path = Found();
        var shade = path.Shade().Sample();
        Assert.Equal(0.75, shade.ValueAt(Channel.Opacity, 110 / 890.0), 9);
        Assert.Equal(1, shade.ValueAt(Channel.Opacity, 670 / 890.0), 9);
        Assert.Equal(0, shade.ValueAt(Channel.Opacity, 1));
        var paper = path.Paper().Sample();
        Assert.Equal(0, paper.ValueAt(Channel.Opacity, (110 + 560 * 0.12) / 890), 9);
        Assert.Equal(1, paper.ValueAt(Channel.Opacity, (110 + 560 * 0.6) / 890), 9);
        var pin = path.Pin();
        Assert.Equal((170.0, 790.0, Fill.Backwards), (pin.Duration, pin.Delay, pin.Fill));
        var hold = path.PrintHold().Sample();
        Assert.Equal(890, hold.Duration);
        Assert.All(hold[Channel.Opacity], o => Assert.Equal(0, o));
    }
}
