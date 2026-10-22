using Tack.Core.Geometry;
using Tack.Core.Reveal;

namespace Tack.Core.Tests.Reveal;

public class ViewTests
{
    static View Shown() => new() { Shown = true, Generation = 4, StayOpen = true, PeekUntil = TimeSpan.FromHours(1) };

    [Fact]
    public void A_tuck_clears_what_kept_the_board_open()
    {
        var view = Shown();
        view.Tip = new Rect(1, 2, 3, 4);
        view.EdgeArmed = true;
        view.SettingsHeld = true;

        Assert.Equal(5UL, view.Tuck());
        Assert.False(view.Shown || view.StayOpen || view.EdgeArmed || view.SettingsHeld);
        Assert.Null(view.PeekUntil);
        Assert.Null(view.Tip);
        // Already tucked: nothing happens, and the generation holds still.
        Assert.Null(view.Tuck());
        Assert.Equal(5UL, view.Generation);
    }

    [Fact]
    public void Holding_a_board_that_is_down_only_sets_the_flag()
    {
        var view = Shown();

        Assert.False(view.HoldForSettings(true), "no reveal needed");
        Assert.True(view.SettingsHeld);
        // Nothing replays: still the same reveal, and the same again.
        Assert.False(view.HoldForSettings(true));
        Assert.True(view.Shown && view.StayOpen && view.Generation == 4);
    }

    [Fact]
    public void Holding_a_tucked_board_asks_for_a_reveal_once()
    {
        var view = new View();

        Assert.True(view.HoldForSettings(true));
        Assert.True(view.SettingsHeld);
        // The reveal came down: asking again (the focus event twice) is not another.
        view.Shown = true;
        Assert.False(view.HoldForSettings(true));
    }

    [Fact]
    public void Letting_go_changes_only_the_flag()
    {
        // Whatever else keeps the board down stays as it was: the poller's rules decide.
        var view = Shown();
        view.HoldForSettings(true);

        Assert.False(view.HoldForSettings(false));
        Assert.False(view.SettingsHeld);
        Assert.True(view.Shown && view.StayOpen && view.PeekUntil.HasValue && view.Generation == 4);
        Assert.False(view.HoldForSettings(false));
        Assert.True(view.Shown && view.Generation == 4);
    }

    [Fact]
    public void A_tuck_while_held_ends_the_hold_until_the_focus_comes_back()
    {
        var view = Shown();
        view.HoldForSettings(true);

        view.Tuck();

        Assert.False(view.SettingsHeld);
        // Letting go afterwards does nothing; gaining the focus again holds and reveals.
        Assert.False(view.HoldForSettings(false));
        Assert.True(view.HoldForSettings(true) && view.SettingsHeld);
    }

    [Fact]
    public void The_board_rect_follows_the_window()
    {
        var view = new View { WindowPos = (100, 50), BoardRect = new Rect(10, 0, 20, 30) };

        var r = view.BoardOnScreen()!.Value;

        Assert.Equal(new Rect(110, 50, 20, 30), r);
        Assert.True(r.Contains(110, 50));
        Assert.False(r.Contains(130, 50));
        Assert.Equal(new Rect(108, 48, 24, 34), r.Inflate(2));
    }

    [Fact]
    public void The_tip_takes_clicks_like_the_board()
    {
        var view = new View { WindowPos = (100, 50), BoardRect = new Rect(10, 0, 20, 30) };
        Assert.False(view.Hit(115, 90));

        view.Tip = new Rect(12, 36, 10, 8);

        Assert.True(view.Hit(115, 90));
        Assert.False(view.Hit(111, 90), "between the tip's edge and the board's, the window lets clicks through");
        Assert.Equal(new Rect(110, 50, 20, 44), view.ZoneOnScreen());
    }

    [Fact]
    public void Rects_unite_intersect_and_convert_to_logical_pixels()
    {
        var a = new Rect(0, 0, 10, 10);
        var b = new Rect(5, -5, 10, 10);
        Assert.Equal(new Rect(0, -5, 15, 15), a.Union(b));
        Assert.Equal(new Rect(5, 0, 5, 5), a.Intersect(b));
        Assert.Null(a.Intersect(new Rect(10, 0, 5, 5)));
        // A snip at (1000, 600) physical on a 200% monitor whose window starts
        // at (0, 0), and on a 150% monitor to the right of it.
        Assert.Equal(new LogicalRect(500, 300, 200, 150), new Rect(1000, 600, 400, 300).ToLogical((0, 0), 2.0));
        var snip = new Rect(3000, 150, 300, 150);
        Assert.Equal(new LogicalRect(80, 100, 200, 100), snip.ToLogical((2880, 0), 1.5));
        Assert.Equal(120, snip.ToLogical((2880, 0), 1.0).X);
    }
}
