using Tack.Core.Pinboard;

namespace Tack.Core.Tests.Pinboard;

public class BoardMetricsTests
{
    [Fact]
    public void The_board_hangs_24_above_the_edge_and_shows_150()
    {
        Assert.Equal(174, BoardMetrics.ElementHeight);
        Assert.Equal(360, BoardMetrics.RailHeight);
        Assert.Equal(-88, BoardMetrics.HingeFrom);
        Assert.Equal(1100, BoardMetrics.Perspective);
    }

    [Fact]
    public void Prints_and_pins_are_sized_as_in_0_2()
    {
        Assert.Equal(30, BoardMetrics.PhotoMinHeight);
        Assert.Equal(90, BoardMetrics.UnknownHeight);
        Assert.Equal(13.65, BoardMetrics.PinSize, 9);
        Assert.Equal(7, BoardMetrics.PinY);
        Assert.Equal(13, BoardMetrics.PrintTop);
    }

    [Fact]
    public void The_pin_colours_are_five_in_palette_order()
    {
        Assert.Equal(
            ["Terracotta", "Sage", "Ink", "Sand", "Graphite"],
            Enum.GetNames<PinColor>());
    }
}
