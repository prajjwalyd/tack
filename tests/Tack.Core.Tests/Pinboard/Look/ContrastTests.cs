using Tack.Core.Pinboard.Look;

namespace Tack.Core.Tests.Pinboard.Look;

/// <summary>Colour arithmetic, against values Tack 0.2's own colour module gave.</summary>
public class ContrastTests
{
    [Fact]
    public void A_colour_round_trips_through_its_hex_in_lowercase()
    {
        Assert.Equal(new Rgb(0x24, 0x50, 0x3f), Rgb.Parse("#24503F"));
        Assert.Equal("#24503f", Rgb.Parse("#24503F").ToHex());
        Assert.Equal("#00ff7f", new Rgb(-3, 300, 127.4).ToHex());
    }

    [Theory]
    [InlineData("24503f")]
    [InlineData("#24503")]
    [InlineData("#24503g")]
    public void Anything_but_a_hex_colour_is_refused(string text) =>
        Assert.Throws<FormatException>(() => Rgb.Parse(text));

    [Fact]
    public void Luminance_and_contrast_are_measured_as_wcag_does()
    {
        Assert.Equal(0.06471270907719218, Contrast.Luminance(Rgb.Parse("#24503f")), 12);
        Assert.Equal(21, Contrast.Ratio(new Rgb(0, 0, 0), new Rgb(255, 255, 255)), 12);
        Assert.Equal(8.196809602728829, Contrast.Ratio(Rgb.Parse("#2a4d8c"), Rgb.Parse("#fffefa")), 12);
    }

    [Fact]
    public void A_colour_laid_over_another_keeps_its_fractions()
    {
        var over = new Rgb(44, 34, 24).Over(0.62, new Rgb(251, 250, 247));
        Assert.Equal(122.66, over.R, 9);
        Assert.Equal(116.08, over.G, 9);
        Assert.Equal(108.74, over.B, 9);
    }

    [Fact]
    public void A_blurred_edge_is_half_covered_at_the_edge_and_fades_over_three_deviations()
    {
        Assert.Equal(0.5, Blur.Cover(0, 2), 9);
        Assert.Equal(0.8413447, Blur.Cover(2, 2), 6);
        Assert.Equal(0.0013499, Blur.Cover(-6, 2), 6);
        Assert.Equal(1, Blur.Cover(0.1, 0));
        Assert.Equal(0, Blur.Cover(-0.1, 0));
    }
}
