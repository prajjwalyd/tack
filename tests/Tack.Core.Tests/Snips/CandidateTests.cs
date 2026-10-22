using Tack.Core.Geometry;
using Tack.Core.Snips;

namespace Tack.Core.Tests.Snips;

public class CandidateTests
{
    [Fact]
    public void The_four_corners_put_the_pointer_on_each_corner()
    {
        var p = (500, 400);
        Assert.Equal(new Rect(400, 350, 100, 50), Corner.BottomRight.RectFor(p, 100, 50));
        Assert.Equal(new Rect(500, 400, 100, 50), Corner.TopLeft.RectFor(p, 100, 50));
        Assert.Equal(new Rect(500, 350, 100, 50), Corner.BottomLeft.RectFor(p, 100, 50));
        Assert.Equal(new Rect(400, 400, 100, 50), Corner.TopRight.RectFor(p, 100, 50));
        var candidates = OriginSearch.Candidates(p, 100, 50, null, null);
        Assert.Equal(4, candidates.Count);
        Assert.Equal(Corner.BottomRight, candidates[0].Corner);
    }

    [Fact]
    public void A_window_or_monitor_of_the_same_size_joins_the_candidates()
    {
        var window = new Rect(10, 20, 801, 599);
        var monitor = new Rect(0, 0, 2880, 1800);

        var candidates = OriginSearch.Candidates((50, 60), 800, 600, window, monitor);

        Assert.Equal(5, candidates.Count);
        Assert.Equal(new Candidate(Source.Window, null, new Rect(10, 20, 800, 600)), candidates[4]);
        Assert.Equal(Source.Monitor, OriginSearch.Candidates((50, 60), 2880, 1800, window, monitor)[^1].Source);
        Assert.Equal(4, OriginSearch.Candidates((50, 60), 640, 480, window, monitor).Count);
    }

    [Fact]
    public void The_search_area_covers_every_candidate_and_the_nudges()
    {
        const int R = 13;

        var area = OriginSearch.SearchArea(OriginSearch.Candidates((500, 400), 100, 50, null, null));

        Assert.Equal(new Rect(400 - R, 350 - R, 200 + 2 * R, 100 + 2 * R), area);
    }

    [Fact]
    public void The_grid_stays_inside_the_picture()
    {
        var points = Samples.GridPoints(100, 7);

        Assert.Equal(Samples.Grid * Samples.Grid, points.Count);
        Assert.All(points, p => Assert.True(p.X < 100 && p.Y < 7));
        Assert.Equal((1, 0), points[0]);
        Assert.Empty(Samples.GridPoints(0, 10));
    }

    [Fact]
    public void A_quick_look_compares_only_the_corner_near_the_pointer()
    {
        var r = new Rect(900, 500, 1600, 900);
        var pointer = (r.X + r.W, r.Y + r.H);

        // The pointer is on the bottom right: the picture's bottom-right patch.
        Assert.Equal(new Rect(1344, 644, 256, 256), OriginSearch.Focus(pointer, Corner.BottomRight.RectFor(pointer, r.W, r.H), 256));
        // On the top left of its own rectangle for the top-left candidate.
        Assert.Equal(new Rect(0, 0, 256, 256), OriginSearch.Focus(pointer, Corner.TopLeft.RectFor(pointer, r.W, r.H), 256));
        // A small picture is compared whole.
        Assert.Equal(new Rect(0, 0, 100, 60), OriginSearch.Focus(pointer, Corner.BottomRight.RectFor(pointer, 100, 60), 256));
        Assert.Equal(new Rect(2500 - 269, 1400 - 269, 538, 538), OriginSearch.FocusArea(pointer, 256));
    }

    [Fact]
    public void The_fallback_is_a_small_rect_of_the_same_shape_on_the_pointer()
    {
        Assert.Equal(new Rect(404, 346, 192, 108), OriginSearch.AroundPointer((500, 400), 1600, 900, 192));
        // Already small: kept at its size.
        Assert.Equal(new Rect(480, 390, 40, 20), OriginSearch.AroundPointer((500, 400), 40, 20, 192));
    }
}
