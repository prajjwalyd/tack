using System.Text.Json;
using Tack.Core.Appearance;
using Tack.Core.Storage.Json;
using Tack.Core.Tests.Storage;

namespace Tack.Core.Tests.Appearance;

public class FeltTests
{
    static string Save(Felt felt) => ConverterText.Write(new FeltJsonConverter(), felt);

    static Felt Load(string json) => ConverterText.Read(new FeltJsonConverter(), json);

    [Fact]
    public void Green_is_the_default()
    {
        Assert.Equal("green", Felt.Default.ToString());
        Assert.Equal("#24503f", Felt.Presets[0].Hex);
    }

    [Fact]
    public void Every_preset_round_trips_by_its_id()
    {
        foreach (var preset in Felt.Presets)
        {
            var felt = Felt.TryParse(preset.Id);
            Assert.Equal(new Felt.PresetFelt(preset), felt);
            Assert.Equal(preset.Id, felt!.ToString());
        }
    }

    [Fact]
    public void The_presets_are_well_formed_and_distinct()
    {
        for (var n = 0; n < Felt.Presets.Count; n++)
        {
            var preset = Felt.Presets[n];
            Assert.All(preset.Id, c => Assert.True(char.IsAsciiLetterLower(c), preset.Id));
            Assert.NotEqual(Felt.AccentId, preset.Id);
            // A preset's hex is a valid custom colour, already in canonical form.
            Assert.Equal(preset.Hex, Felt.TryParse(preset.Hex)!.ToString());
            foreach (var other in Felt.Presets.Skip(n + 1))
            {
                Assert.NotEqual(preset.Id, other.Id);
                Assert.NotEqual(preset.Hex, other.Hex);
            }
        }
    }

    [Fact]
    public void Accent_is_a_value_of_its_own()
    {
        Assert.Equal(Felt.Accent, Felt.TryParse("accent"));
        Assert.Equal("accent", Felt.Accent.ToString());
    }

    [Fact]
    public void A_custom_colour_is_kept_as_seven_lowercase_characters()
    {
        Assert.Equal("#a1b2c3", Felt.Parse("#A1B2C3").ToString());
        Assert.Equal(new Felt.CustomFelt(0xa1, 0xb2, 0xc3), Felt.Parse("#a1b2c3"));
        Assert.Equal("#000000", Felt.Parse("#000000").ToString());
        Assert.Equal("#ffffff", Felt.Parse("#FFFFFF").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("#fff")]
    [InlineData("#1234567")]
    [InlineData("#12345g")]
    [InlineData("123456")]
    [InlineData(" #123456")]
    [InlineData("Green")]
    [InlineData("#é12345")]
    public void Anything_malformed_is_not_a_colour_and_falls_back(string text)
    {
        Assert.Null(Felt.TryParse(text));
        Assert.Equal(Felt.Default, Felt.Parse(text));
    }

    [Fact]
    public void It_is_saved_as_a_plain_string()
    {
        foreach (var text in new[] { "green", "plum", "accent", "#12ab34" })
        {
            var json = Save(Felt.Parse(text));
            Assert.Equal($"\"{text}\"", json);
            Assert.Equal(text, Load(json).ToString());
        }
        Assert.Equal("#abcdef", Load("\"#ABCDEF\"").ToString());
    }
}
