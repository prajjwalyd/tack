using Tack.Core.Reveal;
using Tack.Windows.Edge;

namespace Tack.Windows.Tests.Edge;

public sealed class EdgeWatchTests
{
    static readonly RevealSpeed[] s_speeds = [RevealSpeed.Instant, RevealSpeed.Normal, RevealSpeed.Slow];
    static readonly (int X, int Y) s_atEdge = (800, 0);
    static readonly TimeSpan s_t0 = TimeSpan.FromSeconds(1000);

    static TimeSpan Ms(double n) => TimeSpan.FromMilliseconds(n);

    static EdgeWatch Listening()
    {
        var watch = new EdgeWatch();
        watch.Listen(true);
        return watch;
    }

    /// <summary>
    /// Plays the poller against a pointer that arrives at the edge and rests:
    /// looks when the watch says, and returns when it reveals, in ms.
    /// </summary>
    static double RevealAt(RevealSpeed speed)
    {
        var watch = Listening();
        var dwell = speed.Dwell();
        watch.Hear(EdgeEvent.Arrived, s_t0);
        var t = 0.0;
        while (true)
        {
            var now = s_t0 + Ms(t);
            if (watch.Rested(now, s_atEdge, true, dwell) == Rest.Edge)
            {
                return t;
            }
            var next = watch.NextLook(now, dwell, true, false);
            Assert.NotNull(next);
            t += next.Value.TotalMilliseconds;
        }
    }

    [Theory]
    [InlineData(RevealSpeed.Instant)]
    [InlineData(RevealSpeed.Normal)]
    [InlineData(RevealSpeed.Slow)]
    public void A_pointer_resting_at_the_edge_reveals_at_the_end_of_the_dwell_whatever_the_speed(RevealSpeed speed)
    {
        Assert.Equal(speed.Dwell().TotalMilliseconds, RevealAt(speed));
    }

    [Fact]
    public void A_pointer_that_leaves_before_the_dwell_reveals_nothing()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Slow.Dwell();
        watch.Hear(EdgeEvent.Arrived, s_t0);
        Assert.Equal(Rest.No, watch.Rested(s_t0, s_atEdge, true, dwell));
        watch.Hear(EdgeEvent.Left, s_t0 + dwell - Ms(1));
        // Nothing to look at until it is back; then the count starts afresh.
        Assert.Null(watch.NextLook(s_t0 + dwell, dwell, true, false));
        watch.Hear(EdgeEvent.Arrived, s_t0 + dwell);
        Assert.Equal(Rest.No, watch.Rested(s_t0 + dwell, s_atEdge, true, dwell));
        Assert.Equal(Rest.No, watch.Rested(s_t0 + dwell + dwell - Ms(1), s_atEdge, true, dwell));
        Assert.Equal(Rest.Edge, watch.Rested(s_t0 + dwell + dwell, s_atEdge, true, dwell));
    }

    [Fact]
    public void Jitter_keeps_the_count_and_a_move_starts_it_again()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Normal.Dwell();
        var nudged = (s_atEdge.X + EdgeTiming.EdgeJitter - 1, 1);
        var moved = (s_atEdge.X + EdgeTiming.EdgeJitter, 0);
        Assert.Equal(Rest.No, watch.Rested(s_t0, s_atEdge, true, dwell));
        Assert.Equal(Rest.No, watch.Rested(s_t0 + dwell - Ms(1), nudged, true, dwell));
        Assert.Equal(Rest.Edge, watch.Rested(s_t0 + dwell, nudged, true, dwell));
        // Starts over after a reveal, and when the pointer moves on.
        Assert.Equal(Rest.No, watch.Rested(s_t0 + dwell + Ms(10), s_atEdge, true, dwell));
        Assert.Equal(Rest.No, watch.Rested(s_t0 + dwell + Ms(20), moved, true, dwell));
        Assert.Equal(Rest.No, watch.Rested(s_t0 + dwell + Ms(20) + dwell - Ms(1), moved, true, dwell));
        Assert.Equal(Rest.Edge, watch.Rested(s_t0 + dwell + Ms(20) + dwell, moved, true, dwell));
    }

    [Fact]
    public void A_held_button_is_no_rest()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Instant.Dwell();
        Assert.Equal(Rest.No, watch.Rested(s_t0, s_atEdge, true, dwell));
        // A press resets the count; letting go counts afresh.
        Assert.Equal(Rest.No, watch.Rested(s_t0 + Ms(30), s_atEdge, false, dwell));
        Assert.Equal(Rest.No, watch.Rested(s_t0 + dwell, s_atEdge, true, dwell));
        Assert.Equal(Rest.Edge, watch.Rested(s_t0 + dwell + dwell, s_atEdge, true, dwell));
    }

    [Fact]
    public void A_quicker_speed_reveals_sooner()
    {
        var times = s_speeds.Select(RevealAt).ToArray();
        Assert.True(times[0] < times[1] && times[1] < times[2], string.Join(", ", times));
    }

    [Theory]
    [InlineData(RevealSpeed.Instant)]
    [InlineData(RevealSpeed.Normal)]
    [InlineData(RevealSpeed.Slow)]
    public void A_drag_held_over_the_edge_reveals_after_its_dwell_or_the_speeds_if_shorter(RevealSpeed speed)
    {
        var watch = Listening();
        var dwell = speed.Dwell();
        var wait = EdgeTiming.DragDwell < dwell ? EdgeTiming.DragDwell : dwell;
        watch.Hear(EdgeEvent.DragEntered, s_t0);
        // The button is down: a drag is not an edge rest.
        Assert.Equal(Rest.No, watch.Rested(s_t0 + wait - Ms(1), s_atEdge, false, dwell));
        Assert.Equal(Rest.Drag, watch.Rested(s_t0 + wait, s_atEdge, false, dwell));
        Assert.True(EdgeTiming.DragDwell < RevealSpeed.Slow.Dwell() && RevealSpeed.Instant.Dwell() < EdgeTiming.DragDwell);
    }

    [Fact]
    public void A_drag_that_leaves_the_edge_is_forgotten()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Slow.Dwell();
        watch.Hear(EdgeEvent.DragEntered, s_t0);
        watch.Hear(EdgeEvent.DragLeft, s_t0 + Ms(100));
        Assert.Equal(Rest.No, watch.Rested(s_t0 + EdgeTiming.DragDwell, s_atEdge, false, dwell));
        Assert.Null(watch.NextLook(s_t0 + EdgeTiming.DragDwell, dwell, true, false));
        // Or the look finds the pointer gone from the edge (a strip taken
        // down mid-drag): the poller resets it.
        watch.Hear(EdgeEvent.DragEntered, s_t0 + Ms(300));
        watch.Reset();
        Assert.Equal(Rest.No, watch.Rested(s_t0 + Ms(300) + EdgeTiming.DragDwell, s_atEdge, false, dwell));
    }

    [Fact]
    public void A_drag_is_looked_for_at_the_end_of_its_dwell()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Slow.Dwell();
        watch.Hear(EdgeEvent.DragEntered, s_t0);
        Assert.Equal(EdgeTiming.DragDwell, watch.NextLook(s_t0, dwell, true, false));
        Assert.Equal(Ms(20), watch.NextLook(s_t0 + Ms(200), dwell, true, false));
    }

    [Fact]
    public void Nothing_is_looked_at_until_the_strips_say_so()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Slow.Dwell();
        Assert.Null(watch.NextLook(s_t0, dwell, true, false));
        watch.Hear(EdgeEvent.Arrived, s_t0);
        Assert.Equal(EdgeTiming.Poll, watch.NextLook(s_t0, dwell, true, false));
        // A board just tucked under the pointer looks slowly for it to leave.
        Assert.Equal(EdgeTiming.PollSlow, watch.NextLook(s_t0, dwell, false, false));
        // A full-screen app is asked about, not the pointer.
        Assert.Equal(EdgeTiming.FullScreenCheck, watch.NextLook(s_t0, dwell, true, true));
        watch.Hear(EdgeEvent.Left, s_t0 + Ms(500));
        Assert.Null(watch.NextLook(s_t0 + Ms(500), dwell, true, false));
    }

    [Fact]
    public void Strips_that_are_down_are_not_listened_to()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Slow.Dwell();
        watch.Hear(EdgeEvent.Arrived, s_t0);
        watch.Hear(EdgeEvent.DragEntered, s_t0);
        // The board came down: whatever the strips had said is gone, and what
        // they say on their way out is not true.
        watch.Listen(false);
        watch.Hear(EdgeEvent.Arrived, s_t0);
        watch.Hear(EdgeEvent.DragEntered, s_t0);
        Assert.Null(watch.NextLook(s_t0, dwell, true, false));
        Assert.False(watch.Arrived);
        Assert.Null(watch.DragSince);
        // Up again: heard again, from nothing.
        watch.Listen(true);
        Assert.Null(watch.NextLook(s_t0, dwell, true, false));
        watch.Hear(EdgeEvent.Arrived, s_t0);
        Assert.Equal(EdgeTiming.Poll, watch.NextLook(s_t0, dwell, true, false));
    }

    [Fact]
    public void Without_strips_a_tucked_poller_looks_slowly_and_with_them_when_told()
    {
        Assert.Equal(EdgeTiming.PollSlow, EdgeTiming.TuckedWait(false, null));
        Assert.Equal(EdgeTiming.PollSlow, EdgeTiming.TuckedWait(false, EdgeTiming.Poll));
        Assert.Null(EdgeTiming.TuckedWait(true, null));
        Assert.Equal(EdgeTiming.Poll, EdgeTiming.TuckedWait(true, EdgeTiming.Poll));
    }

    [Fact]
    public void At_the_edge_a_look_is_never_oftener_than_needed_nor_later_than_the_dwell_end()
    {
        var watch = Listening();
        var dwell = RevealSpeed.Slow.Dwell();
        watch.Hear(EdgeEvent.Arrived, s_t0);
        Assert.Equal(Rest.No, watch.Rested(s_t0, s_atEdge, true, dwell));
        Assert.Equal(EdgeTiming.Poll, watch.NextLook(s_t0, dwell, true, false));
        Assert.Equal(Ms(5), watch.NextLook(s_t0 + dwell - Ms(5), dwell, true, false));
    }
}
