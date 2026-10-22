using Tack.Core.Pinboard.Input;
using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Input;

/// <summary>
/// Golden values from Tack 0.2's scroller, run as it shipped with frames 16 ms
/// apart: one session through a row with 600 px to scroll, then a shorter one.
/// </summary>
public class ScrollModelTests
{
    readonly MotionClock _clock = new();
    readonly ScrollModel _scroll;

    public ScrollModelTests() => _scroll = new ScrollModel(_clock);

    double[] Frames(int count, double dt = 16)
    {
        var shown = new List<double>();
        for (var i = 0; i < count && _scroll.Moving; i++)
        {
            _scroll.Step(dt);
            shown.Add(Math.Round(_scroll.Position, 9));
        }
        return [.. shown];
    }

    bool Wheel(double dx, double dy, WheelMode mode = WheelMode.Pixels, bool reduced = false) =>
        _scroll.Wheel(ScrollWheel.Read(dx, dy, mode, pageWidth: 400), revealed: true, reduced);

    static void Same(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 6);
        }
    }

    [Fact]
    public void A_session_matches_the_old_scroller_frame_for_frame()
    {
        _scroll.SetExtent(600, glide: true);

        // A notch at Slow glides on the wheel's 95 ms.
        Assert.True(Wheel(0, 100));
        var notch = Frames(200);
        Assert.Equal(36, notch.Length);
        Same([15.500202885, 28.597842875, 39.665322093, 49.017319579, 56.91973848], notch[..5]);
        Same([99.674093779, 99.724609904, 100], notch[^3..]);
        Assert.Equal(new ScrollEdges(true, true), _scroll.Edges);

        // A touchpad's fine steps follow on its own 22 ms.
        Wheel(0, 7.5);
        Same([103.875811891, 105.748701407, 106.653728595], Frames(3));
        Wheel(0, 12.25);
        Same([113.421553187, 116.691935775, 118.272266667, 119.03592219, 119.404939692, 119.75], Frames(200));

        // At Normal, a second notch while gliding stacks onto the target.
        _clock.SetScale(0.65);
        Wheel(0, 100);
        Same([142.576134166, 160.191944323], Frames(2));
        Wheel(0, 100);
        var stacked = Frames(200);
        Same([196.612880185, 224.720324363, 246.411925621], stacked[..3]);
        Assert.Equal(319.75, stacked[^1]);

        // Lines are 40 px; the target stops at the row's end.
        Wheel(0, 3, WheelMode.Lines);
        var lines = Frames(200);
        Same([347.141361, 368.280333187], lines[..2]);
        Assert.Equal(439.75, lines[^1]);
        Wheel(0, 10, WheelMode.Lines);
        var clamped = Frames(200);
        Same([476.328880002, 504.558215777], clamped[..2]);
        Assert.Equal(600, clamped[^1]);
        Assert.Equal(new ScrollEdges(true, false), _scroll.Edges);

        // A shorter row glides back to its new end on the board's own 110 ms.
        _clock.SetScale(1);
        _scroll.SetExtent(400, glide: true);
        var back = Frames(200);
        Assert.Equal(46, back.Length);
        Same([572.925838102, 549.516727416, 529.276526993], back[..3]);
        Assert.Equal(400, back[^1]);

        // A long frame counts as 48 ms.
        Wheel(0, -100);
        Same([360.334677907, 336.402733581, 321.963472055], Frames(3, 120));
        Frames(500);

        // The larger axis wins, and a sideways step is never a notch.
        Wheel(-60, 20);
        Same([268.993504871, 254.010388745, 246.770171239, 243.271516547, 241.580878849, 240.76392031, 240.369145454, 240], Frames(200));

        // Less motion: there at the next frame.
        Wheel(0, 100, reduced: true);
        Same([340], Frames(5));

        // The board's own scroll glides on 110 ms.
        _scroll.ScrollTo(250, instant: false);
        var to = Frames(200);
        Assert.Equal(41, to.Length);
        Same([327.816627146, 317.282527337], to[..2]);
        Assert.Equal(250, to[^1]);
        Assert.Equal(new ScrollEdges(true, true), _scroll.Edges);

        // A page is the view's width.
        Wheel(0, -1, WheelMode.Pages);
        var page = Frames(200);
        Same([211.249492788, 178.505392813], page[..2]);
        Assert.Equal(0, page[^1]);
        Assert.Equal(new ScrollEdges(false, true), _scroll.Edges);
    }

    [Fact]
    public void A_row_that_fits_or_a_tucked_board_ignores_the_wheel()
    {
        Assert.False(Wheel(0, 100));
        _scroll.SetExtent(600, glide: true);
        Assert.False(_scroll.Wheel(new WheelTurn(100, true), revealed: false, reduced: false));
        Assert.False(_scroll.Moving);
        Assert.Equal(new ScrollEdges(false, false), new ScrollModel(_clock).Edges);
    }

    [Fact]
    public void A_wheel_turn_of_nothing_is_taken_but_moves_nothing()
    {
        _scroll.SetExtent(600, glide: true);
        Assert.True(_scroll.Wheel(new WheelTurn(0, false), revealed: true, reduced: false));
        Assert.False(_scroll.Moving);
    }

    [Fact]
    public void An_instant_scroll_jumps_and_stops_a_glide()
    {
        _scroll.SetExtent(600, glide: true);
        Wheel(0, 100);
        _scroll.Step(16);
        _scroll.ScrollTo(900, instant: true);
        Assert.False(_scroll.Moving);
        Assert.Equal(600, _scroll.Position);
        Assert.Equal(600, _scroll.Target);
    }

    [Fact]
    public void A_shorter_row_on_a_tucked_board_jumps_to_its_end()
    {
        _scroll.SetExtent(600, glide: true);
        _scroll.ScrollTo(500, instant: true);
        _scroll.SetExtent(200, glide: false);
        Assert.False(_scroll.Moving);
        Assert.Equal(200, _scroll.Position);
        _scroll.SetExtent(0, glide: false);
        Assert.Equal(0, _scroll.Position);
        Assert.False(_scroll.Overflows);
        Assert.Equal(new ScrollEdges(false, false), _scroll.Edges);
    }

    [Fact]
    public void A_halt_stops_where_it_is()
    {
        _scroll.SetExtent(600, glide: true);
        Wheel(0, 100);
        _scroll.Step(16);
        _scroll.Halt();
        Assert.False(_scroll.Moving);
        Assert.Equal(_scroll.Position, _scroll.Target);
        Assert.Equal(15.500202885, _scroll.Position, 6);
    }
}
