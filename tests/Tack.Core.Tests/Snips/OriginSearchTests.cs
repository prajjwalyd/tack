using Tack.Core.Geometry;
using Tack.Core.Snips;

namespace Tack.Core.Tests.Snips;

public class OriginSearchTests
{
    static readonly FakeScreen Screen = new();

    [Fact]
    public void The_right_place_scores_one_and_others_far_less()
    {
        var r = new Rect(700, 300, 420, 260);
        var samples = Screen.Snip(r);

        Assert.Equal(1.0, OriginSearch.Score(samples, Screen, (700, 300)));
        Assert.True(OriginSearch.Score(samples, Screen, (701, 300)) < 0.6);
        Assert.True(OriginSearch.Score(samples, Screen, (1120, 560)) < 0.6);
        // Half off the grab: those samples count as misses.
        Assert.True(OriginSearch.Score(samples, Screen, (2700, 300)) < 0.6);
    }

    [Fact]
    public void Small_differences_cost_little()
    {
        var samples = Samples.Of(300, 200, (x, y) =>
        {
            var p = FakeScreen.Busy(100 + x, 100 + y);
            return new Rgb((byte)Math.Min(p.R + 3, 255), (byte)Math.Max(p.G - 3, 0), p.B);
        });

        var score = OriginSearch.Score(samples, Screen, (100, 100));

        Assert.InRange(score, 0.94, 0.9999);
    }

    [Theory]
    [InlineData(Corner.BottomRight)]
    [InlineData(Corner.TopLeft)]
    [InlineData(Corner.BottomLeft)]
    [InlineData(Corner.TopRight)]
    public void Each_corner_is_found_where_the_pointer_says(Corner corner)
    {
        var r = new Rect(900, 500, 640, 360);

        var found = OriginSearch.Find(Screen.Snip(r), Screen, OriginSearch.Candidates(PointerOn(r, corner), r.W, r.H, null, null))!.Value;

        Assert.Equal((r, Source.Corner, corner), (found.Rect, found.Source, found.Corner));
        Assert.Equal(1.0, found.Score);
    }

    [Fact]
    public void A_pointer_that_drifted_a_little_is_forgiven()
    {
        var r = new Rect(900, 500, 300, 180);
        var samples = Screen.Snip(r);

        var found = OriginSearch.Find(samples, Screen, OriginSearch.Candidates((r.X + r.W + 7, r.Y + r.H - 5), r.W, r.H, null, null))!.Value;

        Assert.Equal((r, Corner.BottomRight), (found.Rect, found.Corner));
        // Too far off: nothing is claimed.
        Assert.Null(OriginSearch.Find(samples, Screen, OriginSearch.Candidates((r.X + r.W + 40, r.Y + r.H), r.W, r.H, null, null)));
    }

    [Fact]
    public void A_picture_that_is_not_on_screen_is_not_found()
    {
        // Annotated, or the screen changed: other pixels altogether.
        var samples = Samples.Of(300, 200, (x, y) => FakeScreen.Busy(x * 7 + 3, y * 5 + 11));

        Assert.Null(OriginSearch.Find(samples, Screen, OriginSearch.Candidates((1000, 800), 300, 200, null, null)));
    }

    [Fact]
    public void A_window_snip_is_found_by_the_window_under_the_pointer()
    {
        var window = new Rect(320, 140, 900, 640);

        // A window snip is a click inside the window, not a drag.
        var found = OriginSearch.Find(Screen.Snip(window), Screen, OriginSearch.Candidates((700, 400), 900, 640, window, null))!.Value;

        Assert.Equal((Source.Window, window), (found.Source, found.Rect));
    }

    [Fact]
    public void A_flat_picture_over_flat_screen_goes_by_the_likeliest_corner()
    {
        var flat = new Rgb(240, 240, 240);
        var screen = new FakeScreen { Patch = (new Rect(0, 0, 2880, 900), (_, _) => flat) };
        var samples = Samples.Of(200, 100, (_, _) => flat);

        var found = OriginSearch.Find(samples, screen, OriginSearch.Candidates((1000, 400), 200, 100, null, null))!.Value;

        Assert.True(samples.Detail < 1.0);
        Assert.Equal((Corner.BottomRight, new Rect(800, 300, 200, 100)), (found.Corner, found.Rect));
    }

    [Theory]
    [InlineData(Corner.BottomRight)]
    [InlineData(Corner.TopLeft)]
    [InlineData(Corner.BottomLeft)]
    [InlineData(Corner.TopRight)]
    public void Each_corner_is_found_from_the_patch_near_the_pointer(Corner corner)
    {
        var r = new Rect(700, 400, 1500, 1000);
        var pointer = PointerOn(r, corner);
        Rgb Get(int x, int y) => FakeScreen.Busy(r.X + x, r.Y + y);

        var found = OriginSearch.FindEach(Screen, OriginSearch.Candidates(pointer, r.W, r.H, null, null), c =>
            OriginSearch.Focus(pointer, c.Rect, OriginSearch.FocusHalf) is { } focus ? Samples.Within(r.W, r.H, focus, Get) : null)!.Value;

        Assert.Equal((r, corner), (found.Rect, found.Corner));
        Assert.True(found.Detail > OriginSearch.Plain);
    }

    static (int, int) PointerOn(Rect r, Corner corner) => corner switch
    {
        Corner.BottomRight => (r.X + r.W, r.Y + r.H),
        Corner.TopLeft => (r.X, r.Y),
        Corner.BottomLeft => (r.X, r.Y + r.H),
        _ => (r.X + r.W, r.Y),
    };
}
