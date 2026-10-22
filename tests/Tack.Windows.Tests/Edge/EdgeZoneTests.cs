using Tack.Core.Geometry;
using Tack.Windows.Edge;

namespace Tack.Windows.Tests.Edge;

public sealed class EdgeZoneTests
{
    static Rect Monitor(int x, int y, int w, int h) => new(x, y, w, h);

    [Fact]
    public void The_zone_is_the_middle_third_of_the_top_edge()
    {
        var zone = EdgeZone.Of(Monitor(0, 0, 1920, 1080), EdgeZone.Thickness);
        Assert.Equal(new Rect(640, 0, 640, EdgeZone.Thickness), zone);
        // Centred: as far from the left corner as from the right.
        Assert.Equal(zone.X, 1920 - (zone.X + zone.W));
    }

    [Theory]
    // A 1440 px laptop, 4K and an ultrawide, each in its own pixels.
    [InlineData(1440, 480)]
    [InlineData(3840, 1280)]
    [InlineData(5120, 1707)]
    [InlineData(2560, 853)]
    public void It_scales_with_the_screen(int width, int third)
    {
        Assert.Equal(third, EdgeZone.Of(Monitor(0, 0, width, 1440), EdgeZone.Thickness).W);
    }

    [Fact]
    public void Odd_widths_round_and_stay_inside_the_monitor()
    {
        foreach (var width in new[] { 1366, 1921, 1280, 1, 2, 3, 7 })
        {
            var zone = EdgeZone.Of(Monitor(0, 0, width, 768), EdgeZone.Thickness);
            Assert.True(zone.X >= 0 && zone.X + zone.W <= width, $"{width}: {zone}");
            // The two sides differ by a pixel at most.
            Assert.True(Math.Abs(zone.X - (width - zone.X - zone.W)) <= 1, $"{width}: {zone}");
        }
        Assert.Equal(new Rect(455, 0, 455, 2), EdgeZone.Of(Monitor(0, 0, 1366, 768), 2));
    }

    [Fact]
    public void Each_monitor_has_its_own_zone_on_its_own_top()
    {
        // Another size to the right, a lower one on the left (negative
        // coordinates), another to the right of that.
        var left = Monitor(-2560, 200, 2560, 1440);
        var main = Monitor(0, 0, 1920, 1080);
        var right = Monitor(1920, -300, 3840, 2160);
        Assert.Equal(new Rect(-2560 + 853, 200, 853, 2), EdgeZone.Of(left, 2));
        Assert.Equal(new Rect(640, 0, 640, 2), EdgeZone.Of(main, 2));
        Assert.Equal(new Rect(1920 + 1280, -300, 1280, 2), EdgeZone.Of(right, 2));
        // The corners and the far sides of an edge are outside it.
        foreach (var m in new[] { left, main, right })
        {
            var zone = EdgeZone.Of(m, 2);
            Assert.False(zone.Contains(m.X, m.Y) || zone.Contains(m.X + m.W - 1, m.Y));
            Assert.True(zone.Contains(m.X + m.W / 2, m.Y));
        }
    }
}
