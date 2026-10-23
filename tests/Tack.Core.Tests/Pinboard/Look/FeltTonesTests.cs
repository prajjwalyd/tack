using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;

namespace Tack.Core.Tests.Pinboard.Look;

/// <summary>Golden values from Tack 0.2's own felt colours, run as it shipped.</summary>
public class FeltTonesTests
{
    static readonly Rgb Accent = Rgb.Parse("#0078d4");

    /// <summary>A felt as the setting saves it, and its light and dark felt colours.</summary>
    public static TheoryData<string, string, string> Golden => new()
    {
        { "green", "#24503f", "#1e4234" },
        { "navy", "#233a5e", "#1d304d" },
        { "charcoal", "#3b3a3e", "#303033" },
        { "burgundy", "#682b3b", "#552330" },
        { "plum", "#4a2f5c", "#3d274b" },
        { "teal", "#1f5560", "#19464f" },
        { "slate", "#42586e", "#36485a" },
        { "#ffffff", "#616161", "#505050" },
        { "#000000", "#272727", "#202020" },
        { "#ff0000", "#c60000", "#a20000" },
        { "#a1b2c3", "#58626c", "#485059" },
        { "#102030", "#1c2835", "#17212b" },
        { "#3c7a5a", "#346c4f", "#2b5941" },
        { "#0000ff", "#0000ff", "#0000d1" },
        { "#FFEE00", "#6b6300", "#585100" },
    };

    [Theory]
    [MemberData(nameof(Golden))]
    public void Every_felt_has_the_colours_it_had(string felt, string light, string dark)
    {
        Assert.Equal(light, FeltTones.For(Felt.Parse(felt), Accent, dark: false).Felt.ToHex());
        Assert.Equal(dark, FeltTones.For(Felt.Parse(felt), Accent, dark: true).Felt.ToHex());
    }

    [Fact]
    public void Every_preset_and_any_colour_is_kept_where_the_caption_ink_reads_and_the_wool_shows()
    {
        foreach (var hex in Felt.Presets.Select(p => p.Hex).Concat(["#ffffff", "#000000", "#00ff00", "#123456"]))
        {
            var (light, dark) = FeltTones.Pair(Rgb.Parse(hex));
            Assert.InRange(Contrast.Luminance(light), 0.019, 0.121);
            Assert.InRange(Contrast.Luminance(dark), 0.011, 0.121);
        }
    }

    [Fact]
    public void The_accent_is_darkened_by_a_fixed_mix_and_the_dark_theme_darkens_it_again()
    {
        var light = FeltTones.For(Felt.Accent, Accent, dark: false).Felt;
        var dark = FeltTones.For(Felt.Accent, Accent, dark: true).Felt;
        Assert.Equal(10 * 0.58, light.R, 9);
        Assert.Equal(120 * 0.42 + 14 * 0.58, light.G, 9);
        Assert.Equal(212 * 0.42 + 18 * 0.58, light.B, 9);
        Assert.Equal(light.Times(0.82), dark);
    }

    [Fact]
    public void The_dark_theme_shades_with_black_and_a_dimmer_wash()
    {
        var dark = FeltTones.Of(Rgb.Parse("#1e4234"), dark: true);
        Assert.Equal(new Rgba(0, 0, 0, 0.36), dark.Vignette);
        Assert.Equal(new Rgba(0, 0, 0, 0.48), dark.TuckShade);
        Assert.Equal(0.07, dark.Wash.A);
    }
}
