using Tack.Core.Geometry;
using Tack.Core.Pinboard.Layout;

namespace Tack.Core.Tests.Pinboard.Layout;

/// <summary>Golden values from Tack 0.2's own layout, run as it shipped (scroll targets clamped as its scroller did).</summary>
public class RowLayoutTests
{
    static readonly (bool Note, int W, int H, bool Kept)[] Mixed =
        [(false, 1920, 1080, true), (false, 1080, 1920, true), (true, 0, 0, false), (false, 333, 211, false), (false, 0, 0, false)];

    static readonly (bool Note, int W, int H, bool Kept)[] Many =
        [.. Enumerable.Range(0, 14).Select(i => (false, 1000 + i * 97, 600 + i * 31, i < 3))];

    static RowLayout Lay((bool Note, int W, int H, bool Kept)[] row, double window)
    {
        var entries = row.Select(p =>
        {
            var (w, h) = PrintSize.Of(p.Note, p.W, p.H);
            return new RowEntry(w, h, p.Kept);
        }).ToArray();
        return new RowLayout(entries, window);
    }

    static double[] Shows(RowLayout layout) =>
        [.. Enumerable.Range(0, layout.Count).Select(i => layout.ScrollToShow(i, 0))];

    static bool[] InViewHalfway(RowLayout layout) =>
        [.. Enumerable.Range(0, layout.Count).Select(i => layout.InView(i, layout.ScrollExtent / 2))];

    [Fact]
    public void An_empty_row_is_the_narrowest_board()
    {
        var layout = Lay([], 1920);
        Assert.Equal(360, layout.BoardWidth);
        Assert.Equal(0, layout.ContentWidth);
        Assert.True(layout.Fits);
        Assert.Equal(0, layout.RestScroll());
    }

    [Theory]
    [InlineData(1920)]
    [InlineData(0)]
    public void One_print_sits_in_the_middle(double window)
    {
        var layout = Lay([(false, 1920, 1080, false)], window);
        Assert.Equal(360, layout.BoardWidth);
        Assert.Equal([-84.0], layout.X);
        Assert.Equal(13, RowLayout.Y);
    }

    [Theory]
    [InlineData(1920)]
    [InlineData(1365)]
    [InlineData(0)]
    public void Kept_prints_lead_after_a_wider_gap_and_the_board_fits_them(double window)
    {
        var layout = Lay(Mixed, window);
        Assert.Equal(844, layout.BoardWidth);
        Assert.Equal([-398.0, -212, -108, 46, 230], layout.X);
        Assert.Equal([168.0, 64, 136, 166, 168], layout.Entries.Select(e => e.Width));
        Assert.Equal([98.0, 108, 86, 108, 98], layout.Entries.Select(e => e.Height));
        Assert.True(layout.Fits);
        Assert.Equal(0, layout.ScrollExtent);
        Assert.Equal([0.0, 0, 0, 0, 0], Shows(layout));
    }

    [Fact]
    public void A_row_wider_than_the_window_scrolls()
    {
        var layout = Lay(Mixed, 800);
        Assert.Equal(720, layout.BoardWidth);
        Assert.False(layout.Fits);
        Assert.Equal(124, layout.ScrollExtent);
        Assert.Equal([-336.0, -150, -46, 108, 292], layout.X);
        Assert.Equal(0, layout.RestScroll());
        Assert.Equal([0.0, 0, 0, 0, 124], Shows(layout));
        Assert.Equal([true, true, true, true, true], InViewHalfway(layout));
    }

    [Fact]
    public void Many_prints_on_a_wide_window()
    {
        var layout = Lay(Many, 1920);
        Assert.Equal(1840, layout.BoardWidth);
        Assert.Equal(816, layout.ScrollExtent);
        Assert.Equal([-896.0, -710, -524, -316, -130, 56, 242, 428, 614, 800, 986, 1172, 1358, 1544], layout.X);
        Assert.Equal([104.0, 100, 97, 94, 91, 89, 87, 86, 84, 83, 82, 81, 80, 79], layout.Entries.Select(e => e.Height));
        Assert.Equal(0, layout.RestScroll());
        Assert.Equal([0.0, 0, 0, 0, 0, 0, 0, 0, 0, 72, 258, 444, 630, 816], Shows(layout));
        Assert.Equal(
            [false, false, true, true, true, true, true, true, true, true, true, true, false, false], InViewHalfway(layout));
    }

    [Fact]
    public void Half_pixels_round_up_as_0_2_did()
    {
        var layout = Lay(Many, 1365);
        Assert.Equal(1285, layout.BoardWidth);
        Assert.Equal(1371, layout.ScrollExtent);
        Assert.Equal([-618.0, -432, -246, -38, 148, 334, 520, 706, 892, 1078, 1264, 1450, 1636, 1822], layout.X);
        Assert.Equal([0.0, 0, 0, 0, 0, 0, 69.5, 255.5, 441.5, 627.5, 813.5, 999.5, 1185.5, 1371], Shows(layout));
        Assert.Equal(
            [false, false, false, true, true, true, true, true, true, true, true, false, false, false], InViewHalfway(layout));
        Assert.Equal([-69.0], Lay([(false, 17, 13, false)], 1920).X);
        Assert.Equal([-252.0, -73, 113], Lay([(false, 17, 13, true), (false, 1000, 10, false), (false, 17, 13, false)], 1920).X);
    }

    [Fact]
    public void At_rest_the_row_shows_the_first_unkept_print_when_kept_ones_fill_the_view()
    {
        var layout = Lay(Many, 800);
        Assert.Equal(720, layout.BoardWidth);
        Assert.Equal(1936, layout.ScrollExtent);
        Assert.Equal(76, layout.RestScroll());
        Assert.Equal([0.0, 0, 0, 76, 262, 448, 634, 820, 1006, 1192, 1378, 1564, 1750, 1936], Shows(layout));
        Assert.Equal(
            [false, false, false, false, false, true, true, true, true, false, false, false, false, false], InViewHalfway(layout));
    }

    [Fact]
    public void An_unknown_window_never_limits_the_board()
    {
        var layout = Lay(Many, 0);
        Assert.Equal(2656, layout.BoardWidth);
        Assert.True(layout.Fits);
        Assert.Equal(-1304, layout.X[0]);
    }

    [Fact]
    public void Showing_a_print_already_in_view_keeps_the_scroll()
    {
        var layout = Lay(Many, 800);
        Assert.Equal(500, layout.ScrollToShow(5, 500));
        // To the left of the view: its left edge, less the margin.
        Assert.Equal(36 + 360 - 24, layout.ScrollToShow(2, 900));
    }

    [Theory]
    [InlineData(1920, 360, 1, 772, 376, 158)]
    [InlineData(1920, 1001, 1.25, 564, 1271, 198)]
    [InlineData(1366, 1286, 1.5, 48, 1953, 237)]
    [InlineData(800, 720, 2, 64, 1472, 316)]
    [InlineData(500, 360, 1, 62, 376, 158)]
    [InlineData(1536, 777, 1.75, 650, 1388, 277)]
    [InlineData(2560, 1234, 1.25, 819, 1563, 198)]
    public void The_board_rect_is_the_board_and_its_lift_in_physical_px(
        double window, double width, double scale, int x, int w, int h)
    {
        Assert.Equal(new Rect(x, 0, w, h), RowLayout.BoardRect(window, width, scale));
    }

    [Fact]
    public void The_menu_opens_at_the_print_s_bottom_left_in_physical_px()
    {
        var layout = Lay(Mixed, 800);
        Assert.Equal((852, 167), layout.MenuPoint(4, scroll: 124, window: 800, scale: 1.5));
        Assert.Equal((0, 111), layout.MenuPoint(0, scroll: 124, window: 800, scale: 1));
    }
}
