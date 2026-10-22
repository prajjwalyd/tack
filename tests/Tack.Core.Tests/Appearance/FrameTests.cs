using System.Text.Json;
using Tack.Core.Appearance;
using Tack.Core.Storage.Json;
using Tack.Core.Tests.Storage;

namespace Tack.Core.Tests.Appearance;

public class FrameTests
{
    [Fact]
    public void Aluminium_is_the_default()
    {
        Assert.Equal("aluminium", Frame.Default.Id);
        Assert.Equal("aluminium", Frame.Default.ToString());
    }

    [Fact]
    public void The_finishes_are_the_four_the_board_draws() =>
        Assert.Equal(["aluminium", "graphite", "brass", "silver"], Frame.Presets.Select(p => p.Id));

    [Fact]
    public void Every_finish_round_trips_by_its_id()
    {
        foreach (var preset in Frame.Presets)
        {
            var frame = Frame.TryParse(preset.Id);
            Assert.Equal(preset.Id, frame!.Id);
            Assert.Equal(new Frame(preset), frame);
        }
    }

    [Fact]
    public void The_presets_are_well_formed_and_distinct()
    {
        for (var n = 0; n < Frame.Presets.Count; n++)
        {
            var preset = Frame.Presets[n];
            Assert.All(preset.Id, c => Assert.True(char.IsAsciiLetterLower(c), preset.Id));
            Assert.NotEmpty(preset.Name);
            foreach (var other in Frame.Presets.Skip(n + 1))
            {
                Assert.NotEqual(preset.Id, other.Id);
                Assert.NotEqual(preset.Name, other.Name);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Brass")]
    [InlineData("BRASS")]
    [InlineData(" brass")]
    [InlineData("brass ")]
    [InlineData("gold")]
    [InlineData("#b5a642")]
    [InlineData("accent")]
    [InlineData("\U0001F600")]
    [InlineData("\0")]
    public void Anything_else_is_not_a_finish_and_falls_back(string text)
    {
        Assert.Null(Frame.TryParse(text));
        Assert.Equal(Frame.Default, Frame.Parse(text));
    }

    [Fact]
    public void It_is_saved_as_a_plain_string()
    {
        foreach (var preset in Frame.Presets)
        {
            var json = ConverterText.Write(new FrameJsonConverter(), Frame.Parse(preset.Id));
            Assert.Equal($"\"{preset.Id}\"", json);
            Assert.Equal(preset.Id, ConverterText.Read(new FrameJsonConverter(), json)!.Id);
        }
    }

    [Theory]
    [InlineData("\"gold\"")]
    [InlineData("\"BRASS\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("3")]
    [InlineData("true")]
    [InlineData("[1]")]
    [InlineData("{\"a\":1}")]
    public void A_saved_value_that_is_not_a_finish_reads_as_the_default(string json) =>
        Assert.Equal(Frame.Default, ConverterText.Read(new FrameJsonConverter(), json));
}
