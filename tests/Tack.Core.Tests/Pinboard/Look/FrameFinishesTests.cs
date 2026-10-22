using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;

namespace Tack.Core.Tests.Pinboard.Look;

/// <summary>Tack 0.2's frame tests, with the aluminium frame as it shipped before the setting.</summary>
public class FrameFinishesTests
{
    static readonly bool[] Themes = [false, true];

    static string[] Hex(IEnumerable<Rgb> tones) => [.. tones.Select(t => t.ToHex())];

    static double Chroma(Rgb tone) => (Math.Max(tone.R, Math.Max(tone.G, tone.B)) - Math.Min(tone.R, Math.Min(tone.G, tone.B))) / 255;

    [Fact]
    public void The_finishes_are_the_ones_the_setting_knows_default_first()
    {
        Assert.Equal(Frame.Presets.Select(p => p.Id), FrameFinishes.All.Select(f => f.Id));
        Assert.Equal(Frame.Default.Id, FrameFinishes.Default.Id);
        foreach (var preset in Frame.Presets)
        {
            Assert.Equal(preset.Id, FrameFinishes.For(new Frame(preset)).Id);
        }
    }

    [Fact]
    public void Aluminium_is_exactly_the_frame_the_board_had_before_the_setting()
    {
        var aluminium = FrameFinishes.For(Frame.Default);
        Assert.Equal(["#ffffff", "#e4e7eb", "#d2d6db", "#bcc1c7", "#a6acb3", "#7b818a"], Hex(aluminium.Light.Bottom));
        Assert.Equal(["#e8ebee", "#b9bec4", "#a8aeb5", "#92989f", "#7c828a", "#50555c"], Hex(aluminium.Dark.Bottom));
        Assert.Equal(["#7f858e", "#aab0b7", "#c3c8ce", "#d8dce1", "#ffffff"], Hex(aluminium.Light.Side));
        Assert.Equal(["#565b62", "#80868d", "#969ca3", "#aab0b6", "#e0e3e6"], Hex(aluminium.Dark.Side));
        var (light, dark) = (aluminium.Light.Cap, aluminium.Dark.Cap);
        Assert.Equal(["#eef0f3", "#b3b9c0", "#ffffff", "#767c85"], Hex([light.Top, light.Bottom, light.Lit, light.Shaded]));
        Assert.Equal(["#c6cbd0", "#858b93", "#eef0f2", "#4d5259"], Hex([dark.Top, dark.Bottom, dark.Lit, dark.Shaded]));
    }

    [Fact]
    public void The_rails_stops_are_where_they_were()
    {
        Assert.Equal(
            [new(0, 0), new(0.5, 0), new(0.5, 1), new(1.5, 2), new(3.3, 3), new(5, 4), new(5.5, 5)],
            FrameFinishes.BottomStops.Select(s => s with { At = Math.Round(s.At, 6) }));
        Assert.Equal(
            [new(0, 0), new(0.5, 0), new(1, 1), new(2.7, 2), new(5, 3), new(5.5, 4)],
            FrameFinishes.SideStops.Select(s => s with { At = Math.Round(s.At, 6) }));
    }

    [Fact]
    public void The_sheen_and_the_caps_shade_are_what_they_were()
    {
        Assert.Equal(0.08, RailSheen.Light.Ends);
        Assert.Equal([0.18, 0.30, 0.12], RailSheen.Light.Bands);
        Assert.Equal(0.14, RailSheen.Dark.Ends);
        Assert.Equal([0.14, 0.18, 0.06], RailSheen.Dark.Bands);
        Assert.Equal(new CapShade(Rgb.Parse("#001a0c"), 0.45, 0.2), CapShade.Light);
        Assert.Equal(new CapShade(new Rgb(0, 0, 0), 0.55, 0.28), CapShade.Dark);
    }

    [Fact]
    public void No_two_finishes_are_alike()
    {
        var drawn = FrameFinishes.All.Select(f => string.Join(" ", Hex(f.Light.Bottom.Concat(f.Light.Side).Concat(f.Dark.Bottom))));
        Assert.Equal(FrameFinishes.All.Count, drawn.Distinct().Count());
    }

    [Fact]
    public void Every_finish_has_six_bottom_and_five_side_tones_in_both_themes()
    {
        foreach (var finish in FrameFinishes.All)
        {
            foreach (var dark in Themes)
            {
                Assert.Equal(6, finish.In(dark).Bottom.Count);
                Assert.Equal(5, finish.In(dark).Side.Count);
            }
        }
    }

    [Fact]
    public void An_unknown_finish_gets_the_default() =>
        Assert.Same(FrameFinishes.Default, FrameFinishes.For(Frame.Parse("gold")));

    [Fact]
    public void Every_rail_is_lit_from_above_with_a_real_step_from_the_lit_edge_to_the_shaded_one()
    {
        foreach (var finish in FrameFinishes.All)
        {
            foreach (var dark in Themes)
            {
                var tones = finish.In(dark);
                var bottom = tones.Bottom.Select(Contrast.Luminance).ToArray();
                var side = tones.Side.Select(Contrast.Luminance).ToArray();
                // The bottom rail runs from its lit top face down; a side rail from its shaded outer edge in.
                Assert.Equal(bottom.OrderDescending(), bottom);
                Assert.Equal(side.Order(), side);
                Assert.True(Contrast.Ratio(tones.Bottom[0], tones.Bottom[5]) >= 2, $"{finish.Id} dark={dark} bottom is flat");
                Assert.True(Contrast.Ratio(tones.Side[4], tones.Side[0]) >= 2, $"{finish.Id} dark={dark} side is flat");
            }
        }
    }

    [Fact]
    public void The_dark_theme_is_dimmer_than_the_light_one_so_a_finish_never_glares()
    {
        static double Mean(IEnumerable<Rgb> tones) => tones.Average(Contrast.Luminance);
        foreach (var finish in FrameFinishes.All)
        {
            Assert.True(Mean(finish.Dark.Bottom) < Mean(finish.Light.Bottom), $"{finish.Id} bottom");
            Assert.True(Mean(finish.Dark.Side) < Mean(finish.Light.Side), $"{finish.Id} side");
        }
    }

    [Fact]
    public void The_brass_frame_is_a_quieter_metal_than_the_kept_pins_polished_brass()
    {
        var pin = Chroma(Rgb.Parse("#f2c24b"));
        var brass = FrameFinishes.For(Frame.Parse("brass"));
        foreach (var dark in Themes)
        {
            foreach (var tone in brass.In(dark).Bottom.Concat(brass.In(dark).Side))
            {
                Assert.True(Chroma(tone) <= pin * 0.65, $"dark={dark} {tone} is as vivid as the pin");
            }
        }
    }

    [Fact]
    public void Each_tone_pairs_a_light_and_a_dark_colour_and_the_caps_differ_by_theme()
    {
        var graphite = FrameFinishes.For(Frame.Parse("graphite"));
        Assert.Equal(("#777f89", "#7a808a"), (graphite.Light.Bottom[0].ToHex(), graphite.Dark.Bottom[0].ToHex()));
        Assert.Equal(("#2d3034", "#2a2c30"), (graphite.Light.Side[0].ToHex(), graphite.Dark.Side[0].ToHex()));
        Assert.NotEqual(graphite.Light.Cap, graphite.Dark.Cap);
    }
}
