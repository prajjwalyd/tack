using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;

namespace Tack.Core.Tests.Appearance;

/// <summary>Tack 0.2's finish tests: the frame swatch is drawn from the board's own rail tones.</summary>
public class FinishSwatchTests
{
    static readonly bool[] Themes = [false, true];

    static IEnumerable<Frame> Frames => Frame.Presets.Select(p => new Frame(p));

    [Fact]
    public void Every_finish_has_a_swatch_drawn_from_its_bottom_rail_in_both_themes()
    {
        foreach (var frame in Frames)
        {
            foreach (var dark in Themes)
            {
                var swatch = FinishSwatch.For(frame, dark);
                var tones = FrameFinishes.For(frame).In(dark).Bottom;
                Assert.Equal(tones, swatch.Metal.Select(s => s.Colour.Rgb));
                Assert.All(swatch.Metal, s => Assert.Equal(1, s.Colour.A));
            }
        }
    }

    [Fact]
    public void The_metal_is_lit_from_the_top_at_the_old_stops() =>
        Assert.Equal([0, 0.12, 0.30, 0.56, 0.84, 1], FinishSwatch.For(Frame.Default, false).Metal.Select(s => s.At));

    [Fact]
    public void The_swatches_differ_from_each_other()
    {
        foreach (var dark in Themes)
        {
            var drawn = Frames.Select(f => string.Join(",", FinishSwatch.For(f, dark).Metal.Select(s => s.Colour.Rgb.ToHex())));
            Assert.Equal(Frame.Presets.Count, drawn.Distinct().Count());
        }
    }

    [Fact]
    public void The_check_mark_is_readable_on_the_face_of_every_finish_in_both_themes()
    {
        foreach (var frame in Frames)
        {
            foreach (var dark in Themes)
            {
                var face = FrameFinishes.For(frame).In(dark).Bottom[3];
                Assert.True(Contrast.Ratio(FinishSwatch.For(frame, dark).Ink, face) >= 3, $"{frame} dark={dark}");
            }
        }
    }

    [Fact]
    public void The_ink_is_white_or_near_black_whichever_reads_better()
    {
        Assert.Equal(FinishSwatch.Light, FinishSwatch.InkOn(Rgb.Parse("#45494f")));
        Assert.Equal(FinishSwatch.Dark, FinishSwatch.InkOn(Rgb.Parse("#bcc1c7")));
        Assert.Equal(FinishSwatch.Dark, FinishSwatch.For(Frame.Default, false).Ink);
        Assert.Equal(FinishSwatch.Light, FinishSwatch.For(Frame.Parse("graphite"), false).Ink);
    }

    [Fact]
    public void The_glow_is_the_opposite_tone_of_the_ink()
    {
        Assert.Equal(new Rgba(0, 0, 0, 0.55), FinishSwatch.For(Frame.Parse("graphite"), true).Glow);
        Assert.Equal(new Rgba(255, 255, 255, 0.6), FinishSwatch.For(Frame.Default, false).Glow);
    }

    [Fact]
    public void The_shine_and_grain_are_softer_in_the_dark_theme()
    {
        var (light, dark) = (FinishSwatch.For(Frame.Default, false), FinishSwatch.For(Frame.Default, true));
        Assert.Equal((0.5, 0.3), (light.Shine, dark.Shine));
        Assert.Equal(0.34, light.Grain.Max(s => s.Colour.A));
        Assert.Equal(0.2, dark.Grain.Max(s => s.Colour.A));
        Assert.All(light.Grain.Concat(dark.Grain), s => Assert.Equal(new Rgb(255, 255, 255), s.Colour.Rgb));
        Assert.Equal((0, 1), (light.Grain[0].At, light.Grain[^1].At));
        Assert.Equal((0, 0), (light.Grain[0].Colour.A, light.Grain[^1].Colour.A));
    }
}
