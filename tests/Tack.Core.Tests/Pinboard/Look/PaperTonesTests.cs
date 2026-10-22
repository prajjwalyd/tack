using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;

namespace Tack.Core.Tests.Pinboard.Look;

/// <summary>Tack 0.2's paper tests, and golden values from its own paper colours run as it shipped.</summary>
public class PaperTonesTests
{
    static readonly Rgb LinkBlue = Rgb.Parse("#2a4d8c");

    /// <summary>Each paper's light paper, light link, dark paper and dark link, as 0.2 made them.</summary>
    public static TheoryData<string, string, string, string, string> Golden => new()
    {
        { "white", "#fffefa", "#2a4d8c", "#eeebe4", "#2a4d8c" },
        { "cream", "#faf0d7", "#2a4d8c", "#e9dec4", "#284a86" },
        { "butter", "#fff0a8", "#2a4d8c", "#eede99", "#274884" },
        { "blush", "#fadadd", "#294b89", "#e9caca", "#234176" },
        { "sky", "#d6e8fa", "#2a4d8c", "#c8d7e4", "#25447b" },
        { "mint", "#d5f0de", "#2a4d8c", "#c7deca", "#26457e" },
    };

    static double InkOn(Rgb paper) => Contrast.Ratio(PaperTones.Ink.Rgb.Over(PaperTones.Ink.A, paper), paper);

    [Theory]
    [MemberData(nameof(Golden))]
    public void Every_paper_has_the_colours_it_had(string id, string light, string lightLink, string dark, string darkLink)
    {
        var paper = NotePaper.Parse(id);
        Assert.Equal((light, lightLink), (PaperTones.For(paper, false).Paper.ToHex(), PaperTones.For(paper, false).Link.ToHex()));
        Assert.Equal((dark, darkLink), (PaperTones.For(paper, true).Paper.ToHex(), PaperTones.For(paper, true).Link.ToHex()));
    }

    [Fact]
    public void White_is_exactly_the_card_the_notes_had_before_the_setting()
    {
        var light = PaperTones.For(NotePaper.Default, dark: false);
        var dark = PaperTones.For(NotePaper.Default, dark: true);
        Assert.Equal(("#fffefa", "#eeebe4"), (light.Paper.ToHex(), dark.Paper.ToHex()));
        Assert.Equal(LinkBlue, light.Link);
        Assert.Equal(LinkBlue, dark.Link);
        Assert.Equal(new Rgba(42, 77, 140, 0.8), light.Domain);
        Assert.Equal(new Rgba(42, 77, 140, 0.8), dark.Domain);
    }

    [Fact]
    public void The_dark_theme_dims_every_paper_the_way_it_dims_the_white_card()
    {
        Assert.Equal(Rgb.Parse("#eeebe4"), PaperTones.Dimmed(Rgb.Parse("#fffefa")));
        foreach (var preset in NotePaper.Presets)
        {
            var paper = new NotePaper(preset);
            var (light, dark) = (PaperTones.For(paper, false).Paper, PaperTones.For(paper, true).Paper);
            Assert.Equal(Rgb.Parse(preset.Hex), light);
            Assert.True(dark.R < light.R && dark.G < light.G && dark.B < light.B, preset.Id);
        }
    }

    [Fact]
    public void The_ink_reads_on_every_paper_in_both_themes_at_7_to_1_or_better()
    {
        foreach (var preset in NotePaper.Presets)
        {
            foreach (var dark in new[] { false, true })
            {
                var ratio = InkOn(PaperTones.For(new NotePaper(preset), dark).Paper);
                Assert.True(ratio >= 7, $"{preset.Id} dark={dark}: {ratio:0.00}");
            }
        }
    }

    [Fact]
    public void The_link_colour_reads_on_every_paper_in_both_themes()
    {
        foreach (var preset in NotePaper.Presets)
        {
            foreach (var dark in new[] { false, true })
            {
                var tones = PaperTones.For(new NotePaper(preset), dark);
                var ratio = Contrast.Ratio(tones.Link, tones.Paper);
                Assert.True(ratio >= PaperTones.LinkMinContrast, $"{preset.Id} dark={dark}: {ratio:0.00}");
            }
        }
    }

    [Fact]
    public void A_link_keeps_its_blue_unless_the_paper_would_make_it_too_faint()
    {
        var cream = Rgb.Parse("#faf0d7");
        Assert.Equal(LinkBlue, PaperTones.Of(cream, dark: false).Link);
        var dark = PaperTones.Of(cream, dark: true).Link;
        Assert.True(dark.R <= LinkBlue.R && dark.G <= LinkBlue.G && dark.B <= LinkBlue.B);
        Assert.NotEqual(LinkBlue, dark);
    }

    [Fact]
    public void A_paper_too_dark_for_any_blue_gets_black_links() =>
        Assert.Equal(new Rgb(0, 0, 0), PaperTones.LinkOn(Rgb.Parse("#202020")));
}
