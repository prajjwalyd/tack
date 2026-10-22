using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>Golden values from Tack 0.2's own spring formula, run as it shipped.</summary>
public class SpringTests
{
    static readonly double[] RevealSamples =
    [
        0, 0.0307, 0.0829, 0.1509, 0.2295, 0.3144, 0.4021, 0.4893, 0.5738, 0.6536, 0.7275, 0.7944, 0.854, 0.9058,
        0.9501, 0.987, 1.0169, 1.0405, 1.0582, 1.0707, 1.0788, 1.083, 1.084, 1.0824, 1.0788, 1.0736, 1.0674,
        1.0605, 1.0533, 1.0459, 1.0388, 1.0319, 1.0255, 1.0196, 1.0144, 1.0097, 1.0058, 1.0024, 0.9996, 0.9974, 1,
    ];

    static readonly double[] LayoutSamples =
    [
        0, 0.0118, 0.0429, 0.0878, 0.1419, 0.2017, 0.2645, 0.328, 0.3907, 0.4513, 0.509, 0.5632, 0.6136, 0.66,
        0.7024, 0.7408, 0.7754, 0.8063, 0.8338, 0.8581, 0.8795, 0.8982, 0.9145, 0.9286, 0.9408, 0.9512, 0.9601,
        0.9677, 0.974, 0.9794, 0.9839, 0.9876, 0.9906, 0.9931, 0.9952, 0.9968, 0.9981, 0.9991, 0.9998, 1.0004, 1,
    ];

    static readonly double[] SettleSamples =
    [
        0, 0.0125, 0.0463, 0.0961, 0.1574, 0.2265, 0.2999, 0.3752, 0.45, 0.5227, 0.592, 0.6569, 0.7166, 0.771,
        0.8196, 0.8626, 0.9001, 0.9323, 0.9595, 0.982, 1.0004, 1.015, 1.0262, 1.0345, 1.0402, 1.0438, 1.0456,
        1.0459, 1.0451, 1.0433, 1.0409, 1.038, 1.0347, 1.0313, 1.0279, 1.0244, 1.0211, 1.018, 1.0151, 1.0124, 1,
    ];

    [Fact]
    public void The_reveal_swing_overshoots_flat_and_lasts_477_ms()
    {
        Assert.Equal(477, Spring.Reveal.Ms);
        Assert.Equal(RevealSamples, Spring.Reveal.Samples);
    }

    [Fact]
    public void The_row_slides_without_a_visible_overshoot_in_452_ms()
    {
        Assert.Equal(452, Spring.Layout.Ms);
        Assert.Equal(LayoutSamples, Spring.Layout.Samples);
    }

    [Fact]
    public void A_print_settles_after_hover_with_a_whisper_of_bounce_in_314_ms()
    {
        Assert.Equal(314, Spring.Settle.Ms);
        Assert.Equal(SettleSamples, Spring.Settle.Samples);
    }

    [Fact]
    public void A_spring_is_41_samples_from_0_ending_exactly_at_1()
    {
        var spring = new Spring(0.5, 0.5);
        Assert.Equal(Spring.Steps + 1, spring.Samples.Count);
        Assert.Equal(0, spring.Samples[0]);
        Assert.Equal(1, spring.Samples[^1]);
    }

    [Fact]
    public void Its_easing_passes_through_every_sample_and_is_straight_between()
    {
        var easing = Spring.Reveal.Easing;
        for (var i = 0; i <= Spring.Steps; i++)
        {
            Assert.Equal(RevealSamples[i], easing.At(i / (double)Spring.Steps), 12);
        }
        var between = (RevealSamples[20] + RevealSamples[21]) / 2;
        Assert.Equal(between, easing.At(20.5 / Spring.Steps), 12);
        Assert.Equal(Spring.Steps + 1, easing.Knots.Count);
    }
}
