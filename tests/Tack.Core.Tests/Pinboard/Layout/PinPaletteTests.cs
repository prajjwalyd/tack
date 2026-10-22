using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Layout;

namespace Tack.Core.Tests.Pinboard.Layout;

/// <summary>Golden values from Tack 0.2's own pin colours, run as they shipped.</summary>
public class PinPaletteTests
{
    [Theory]
    [InlineData("a", PinColor.Terracotta)]
    [InlineData("b", PinColor.Ink)]
    [InlineData("print-1", PinColor.Sage)]
    [InlineData("print-2", PinColor.Ink)]
    [InlineData("12345", PinColor.Graphite)]
    [InlineData("", PinColor.Sage)]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", PinColor.Terracotta)]
    [InlineData("élan", PinColor.Ink)]
    [InlineData("😀", PinColor.Ink)]
    [InlineData("0f769ff", PinColor.Sage)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", PinColor.Terracotta)]
    public void A_first_colour_comes_from_the_id(string id, PinColor expected)
    {
        Assert.Equal(expected, PinPalette.ColorFor(id));
    }

    public static TheoryData<(PinColor, bool, bool)[], PinColor[]> Rows => new()
    {
        // The newer of two clashing neighbours changes.
        { [(PinColor.Sage, false, true), (PinColor.Sage, false, false)], [PinColor.Sage, PinColor.Terracotta] },
        { [(PinColor.Ink, false, false), (PinColor.Ink, false, true)], [PinColor.Terracotta, PinColor.Ink] },
        // Two unsettled: the right one changes, and the third then no longer clashes.
        {
            [(PinColor.Ink, false, false), (PinColor.Ink, false, false), (PinColor.Ink, false, false)],
            [PinColor.Ink, PinColor.Terracotta, PinColor.Ink]
        },
        // A kept pin is brass, so it never clashes.
        {
            [(PinColor.Sand, false, true), (PinColor.Sand, true, true), (PinColor.Sand, false, false)],
            [PinColor.Sand, PinColor.Sand, PinColor.Sand]
        },
        // The new colour is not that of the two before nor the one after.
        {
            [(PinColor.Terracotta, false, true), (PinColor.Sage, false, true), (PinColor.Sage, false, false), (PinColor.Terracotta, false, true), (PinColor.Ink, false, true)],
            [PinColor.Terracotta, PinColor.Sage, PinColor.Ink, PinColor.Terracotta, PinColor.Ink]
        },
        {
            [(PinColor.Graphite, false, false), (PinColor.Graphite, false, false), (PinColor.Graphite, false, false), (PinColor.Graphite, false, false), (PinColor.Graphite, false, false)],
            [PinColor.Graphite, PinColor.Terracotta, PinColor.Graphite, PinColor.Sage, PinColor.Graphite]
        },
        {
            [(PinColor.Terracotta, false, true), (PinColor.Sage, false, true), (PinColor.Ink, false, false), (PinColor.Ink, false, true), (PinColor.Sand, false, true)],
            [PinColor.Terracotta, PinColor.Sage, PinColor.Terracotta, PinColor.Ink, PinColor.Sand]
        },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Neighbouring_unkept_pins_never_share_a_colour((PinColor, bool, bool)[] row, PinColor[] expected)
    {
        var pins = row.Select(p => new PinSlot(p.Item1, p.Item2, p.Item3)).ToArray();
        Assert.Equal(expected, PinPalette.Fix(pins));
    }

    [Fact]
    public void An_empty_row_has_no_colours()
    {
        Assert.Empty(PinPalette.Fix([]));
    }
}
