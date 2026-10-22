using Tack.Core.Appearance;
using Tack.Core.Settings;

namespace Tack.Core.Tests.Appearance;

/// <summary>Tack 0.2's appearance tests: what Reset appearance goes back to, and when there is nothing to reset.</summary>
public class AppearanceDefaultsTests
{
    [Fact]
    public void A_reset_goes_back_to_the_first_of_each_list()
    {
        var reset = new AppearanceSettings();
        Assert.Equal(Felt.Presets[0].Id, reset.Felt.ToString());
        Assert.Equal(Frame.Presets[0].Id, reset.Frame.Id);
        Assert.Equal(NotePaper.Presets[0].Id, reset.Paper.Id);
    }

    [Fact]
    public void There_is_nothing_to_reset_when_everything_is_the_default() =>
        Assert.True(AppearanceDefaults.IsDefault(new AppearanceSettings()));

    [Theory]
    [InlineData("accent", "aluminium", "white")]
    [InlineData("#a1b2c3", "aluminium", "white")]
    [InlineData("#24503f", "aluminium", "white")]
    [InlineData("green", "silver", "white")]
    [InlineData("green", "aluminium", "sky")]
    public void Each_setting_alone_is_enough_to_enable_it(string felt, string frame, string paper) =>
        Assert.False(AppearanceDefaults.IsDefault(new AppearanceSettings
        {
            Felt = Felt.Parse(felt),
            Frame = Frame.Parse(frame),
            Paper = NotePaper.Parse(paper),
        }));

    [Fact]
    public void The_defaults_parsed_back_from_their_ids_are_the_defaults() =>
        Assert.True(AppearanceDefaults.IsDefault(new AppearanceSettings
        {
            Felt = Felt.Parse("green"),
            Frame = Frame.Parse("aluminium"),
            Paper = NotePaper.Parse("white"),
        }));
}
