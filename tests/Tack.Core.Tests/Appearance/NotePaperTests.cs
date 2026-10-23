using System.Globalization;
using System.Text.Json;
using Tack.Core.Appearance;
using Tack.Core.Storage.Json;
using Tack.Core.Tests.Storage;

namespace Tack.Core.Tests.Appearance;

public class NotePaperTests
{
    [Fact]
    public void White_is_the_default_and_the_card_it_always_was()
    {
        Assert.Equal("white", NotePaper.Default.Id);
        Assert.Equal("#fffefa", NotePaper.Presets[0].Hex);
    }

    [Fact]
    public void Every_colour_round_trips_by_its_id()
    {
        foreach (var preset in NotePaper.Presets)
        {
            var paper = NotePaper.TryParse(preset.Id);
            Assert.Equal(preset.Id, paper!.Id);
            Assert.Equal(new NotePaper(preset), paper);
        }
    }

    [Fact]
    public void The_presets_are_well_formed_and_distinct()
    {
        for (var n = 0; n < NotePaper.Presets.Count; n++)
        {
            var preset = NotePaper.Presets[n];
            Assert.All(preset.Id, c => Assert.True(char.IsAsciiLetterLower(c), preset.Id));
            Assert.NotEmpty(preset.Name);
            Assert.Matches("^#[0-9a-f]{6}$", preset.Hex);
            foreach (var other in NotePaper.Presets.Skip(n + 1))
            {
                Assert.NotEqual(preset.Id, other.Id);
                Assert.NotEqual(preset.Hex, other.Hex);
            }
        }
    }

    [Fact]
    public void Every_paper_is_pale_enough_for_dark_ink()
    {
        // The paper's relative luminance. The note's ink (28, 32, 42 at 86%)
        // reads on anything this pale at 7:1 or better.
        foreach (var preset in NotePaper.Presets)
        {
            double Channel(int at)
            {
                var v = int.Parse(preset.Hex.AsSpan(at, 2), NumberStyles.HexNumber) / 255.0;
                return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            var luminance = 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
            Assert.True(luminance > 0.6, $"{preset.Id} is too dark: {luminance:F2}");
        }
    }

    [Theory]
    [InlineData("Blush")]
    [InlineData("pink")]
    public void Anything_else_is_not_a_colour_and_falls_back(string text)
    {
        Assert.Null(NotePaper.TryParse(text));
        Assert.Equal(NotePaper.Default, NotePaper.Parse(text));
    }

    [Fact]
    public void It_is_saved_as_a_plain_string()
    {
        foreach (var preset in NotePaper.Presets)
        {
            var json = ConverterText.Write(new NotePaperJsonConverter(), NotePaper.Parse(preset.Id));
            Assert.Equal($"\"{preset.Id}\"", json);
            Assert.Equal(preset.Id, ConverterText.Read(new NotePaperJsonConverter(), json)!.Id);
        }
    }
}
