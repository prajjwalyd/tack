using Tack.Core.Appearance;

namespace Tack.Core.Tests.Appearance;

/// <summary>The board colour card: its swatches, which one a felt is, what its head says and where the picker starts.</summary>
public class FeltChoicesTests
{
    [Fact]
    public void The_presets_come_first_then_accent_then_custom()
    {
        Assert.Equal(
            ["green", "navy", "charcoal", "burgundy", "plum", "teal", "slate", "accent", "custom"],
            FeltChoices.All.Select(c => c.Value));
        Assert.Equal(new SwatchChoice("accent", "Accent", "Windows accent colour"), FeltChoices.All[^2]);
        Assert.Equal(new SwatchChoice("custom", "Custom", "Custom colour", Manual: true), FeltChoices.All[^1]);
        Assert.Equal(1, FeltChoices.All.Count(c => c.Manual));
        Assert.Equal(new SwatchChoice("navy", "Navy", "Navy"), FeltChoices.All[1]);
    }

    [Fact]
    public void Nine_swatches_fill_the_grid_every_card_shares() =>
        Assert.Equal(FeltChoices.Columns, FeltChoices.All.Count);

    [Theory]
    [InlineData("plum", "plum", "Plum")]
    [InlineData("accent", "accent", "Accent")]
    [InlineData("#A1B2C3", "custom", "Custom #a1b2c3")]
    public void A_felt_is_one_swatch_and_names_itself(string felt, string value, string name)
    {
        Assert.Equal(value, FeltChoices.ValueOf(Felt.Parse(felt)));
        Assert.Equal(name, FeltChoices.NameOf(Felt.Parse(felt)));
    }

    [Fact]
    public void A_swatch_is_a_felt_except_custom_which_opens_the_picker()
    {
        Assert.Equal(Felt.Parse("teal"), FeltChoices.FeltOf("teal"));
        Assert.Equal(Felt.Accent, FeltChoices.FeltOf("accent"));
        Assert.Null(FeltChoices.FeltOf("custom"));
        Assert.Null(FeltChoices.FeltOf("#a1b2c3"));
    }

    [Fact]
    public void The_picker_starts_on_the_last_custom_colour()
    {
        var last = new Felt.CustomFelt(1, 2, 3);
        Assert.Equal(last, FeltChoices.PickerStart(Felt.Parse("navy"), last));
        Assert.Equal(last, FeltChoices.PickerStart(Felt.Accent, last));
    }

    [Fact]
    public void Else_on_the_preset_the_board_has_else_on_the_first()
    {
        Assert.Equal(Felt.Parse("#233a5e"), FeltChoices.PickerStart(Felt.Parse("navy"), null));
        Assert.Equal(Felt.Parse("#24503f"), FeltChoices.PickerStart(Felt.Accent, null));
        Assert.Equal(Felt.Parse("#a1b2c3"), FeltChoices.PickerStart(Felt.Parse("#a1b2c3"), null));
    }
}
