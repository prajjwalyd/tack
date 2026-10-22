using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;

namespace Tack.Core.Tests.Pinboard.Look;

/// <summary>Tack 0.2's hint card tests, and golden values from its own hint colours run as it shipped.</summary>
public class HintTonesTests
{
    /// <summary>Each paper's hint card, sentence and stress alphas, light then dark, as 0.2 made them.</summary>
    public static TheoryData<string, string, double, double, string, double, double> Golden => new()
    {
        { "white", "#fbfaf7", 0.62, 0.82, "#e9e6df", 0.62, 0.82 },
        { "cream", "#f6ecd4", 0.64, 0.82, "#e4d9c0", 0.64, 0.83 },
        { "butter", "#fbeca6", 0.64, 0.82, "#e9d996", 0.64, 0.83 },
        { "blush", "#f6d7da", 0.66, 0.82, "#e4c6c6", 0.66, 0.87 },
        { "sky", "#d3e4f7", 0.65, 0.82, "#c4d2df", 0.66, 0.85 },
        { "mint", "#d2ecdb", 0.65, 0.82, "#c3d9c6", 0.65, 0.85 },
    };

    static double Reads(Rgba ink, Rgb paper) => Contrast.Ratio(ink.Rgb.Over(ink.A, paper), paper);

    [Theory]
    [MemberData(nameof(Golden))]
    public void Every_paper_gives_the_hint_it_had(string id, string light, double soft, double strong, string dark, double darkSoft, double darkStrong)
    {
        var paper = NotePaper.Parse(id);
        var (l, d) = (HintTones.For(paper, false), HintTones.For(paper, true));
        Assert.Equal((light, soft, strong), (l.Paper.ToHex(), l.Ink.A, l.InkStrong.A));
        Assert.Equal((dark, darkSoft, darkStrong), (d.Paper.ToHex(), d.Ink.A, d.InkStrong.A));
    }

    [Fact]
    public void White_is_exactly_the_hint_card_the_empty_board_had_before_it_followed_the_paper()
    {
        var light = HintTones.For(NotePaper.Default, dark: false);
        var dark = HintTones.For(NotePaper.Default, dark: true);
        Assert.Equal(("#fbfaf7", "#e9e6df"), (light.Paper.ToHex(), dark.Paper.ToHex()));
        foreach (var tones in new[] { light, dark })
        {
            Assert.Equal(new Rgba(44, 34, 24, 0.62), tones.Ink);
            Assert.Equal(new Rgba(44, 34, 24, 0.82), tones.InkStrong);
        }
    }

    [Fact]
    public void The_hints_ink_reads_on_every_paper_the_stress_at_7_to_1_the_sentence_as_well_as_on_white()
    {
        foreach (var dark in new[] { false, true })
        {
            var white = HintTones.For(NotePaper.Default, dark);
            var floor = Reads(white.Ink, white.Paper);
            foreach (var preset in NotePaper.Presets)
            {
                var tones = HintTones.For(new NotePaper(preset), dark);
                Assert.True(Reads(tones.InkStrong, tones.Paper) >= 7, $"{preset.Id} dark={dark} stress");
                Assert.True(Reads(tones.Ink, tones.Paper) >= floor - 1e-9, $"{preset.Id} dark={dark} sentence");
            }
        }
    }

    [Fact]
    public void The_hint_card_is_cut_from_the_note_paper_lighter_than_it_nowhere()
    {
        foreach (var preset in NotePaper.Presets)
        {
            foreach (var dark in new[] { false, true })
            {
                var (note, hint) = (PaperTones.For(new NotePaper(preset), dark).Paper, HintTones.For(new NotePaper(preset), dark).Paper);
                Assert.True(hint.R <= note.R && hint.G <= note.G && hint.B <= note.B, $"{preset.Id} dark={dark}");
            }
        }
    }
}
