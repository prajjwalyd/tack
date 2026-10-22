using Tack.Core.Geometry;
using Tack.Core.Pinboard.Input;

namespace Tack.Core.Tests.Pinboard.Input;

public class HoverLeanTests
{
    static readonly LogicalRect Slot = new(100, 20, 168, 108);

    [Fact]
    public void The_middle_of_the_print_is_level_and_leans_nowhere()
    {
        var lean = HoverLean.At(Slot, 184, 74);
        Assert.Equal(0, lean.Nx, 9);
        Assert.Equal(0, lean.Ny, 9);
        Assert.Equal(0, lean.Degrees, 9);
    }

    [Fact]
    public void The_corners_map_to_minus_one_and_one()
    {
        var topLeft = HoverLean.At(Slot, 100, 20);
        Assert.Equal((-1, -1), (topLeft.Nx, topLeft.Ny));
        var bottomRight = HoverLean.At(Slot, 268, 128);
        Assert.Equal((1, 1), (bottomRight.Nx, bottomRight.Ny));
    }

    [Fact]
    public void The_free_end_swings_toward_the_pointer_more_the_lower_it_is()
    {
        // -nx 0.9 (0.35 + 0.65 (ny + 1) / 2)
        Assert.Equal(-0.9, HoverLean.At(Slot, 268, 128).Degrees, 9);
        Assert.Equal(0.9 * 0.35, HoverLean.At(Slot, 100, 20).Degrees, 9);
        Assert.Equal(-0.9 * 0.675, HoverLean.At(Slot, 268, 74).Degrees, 9);
    }

    [Fact]
    public void A_pointer_outside_the_print_is_held_to_its_edge()
    {
        var lean = HoverLean.At(Slot, -500, 900);
        Assert.Equal((-1, 1), (lean.Nx, lean.Ny));
        Assert.Equal(0.9, lean.Degrees, 9);
    }

    [Fact]
    public void An_empty_rect_leans_nowhere()
    {
        var lean = HoverLean.At(new LogicalRect(0, 0, 0, 0), 5, 5);
        Assert.Equal(new HoverLean(0, 0, 0), lean);
    }
}
