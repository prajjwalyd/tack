using Tack.Core.Appearance;

namespace Tack.Core.Pinboard.Look;

/// <summary>
/// The felt's colours in one theme: the felt itself and the light and shade
/// laid over it, all from the felt's own hue. Everything else on the board
/// (frame, paper, pins, the caption ink) stays as it is, so the felt is kept
/// to what they read on: dark enough for the light caption ink and the white
/// pins, never so dark its wool and shadows vanish.
/// </summary>
/// <param name="Felt">The felt colour.</param>
/// <param name="Wash">Light from above, brightest at the top edge.</param>
/// <param name="Vignette">The falloff towards the sides and bottom.</param>
/// <param name="InnerShade">The fine shade along the rails' inner edge.</param>
/// <param name="Crease">The soft shadow where the felt meets the rails.</param>
/// <param name="TuckShade">The shade where the board tucks under the screen edge.</param>
/// <param name="Unlit">The board facing away from the light while folded up.</param>
/// <param name="Fade">The felt colour again, over the wool: how strongly the wool shows.</param>
public sealed record FeltTones(
    Rgb Felt, Rgba Wash, Rgba Vignette, Rgba InnerShade, Rgba Crease, Rgba TuckShade, Rgba Unlit, Rgba Fade)
{
    /// <summary>How strongly the wool shows: 0 flat felt, 1 the textures as drawn.</summary>
    public const double Grain = 0.8;

    /// <summary>The most luminance a felt may have: the caption ink still reads on it at 4.5:1.</summary>
    const double MaxLuma = 0.12;

    const double MinLuma = 0.02;
    const double DarkMinLuma = 0.012;

    /// <summary>The dark theme's felt is the light one's channels times this.</summary>
    const double DarkScale = 0.82;

    /// <summary>The Windows accent colour is darkened by a fixed mix, not fitted.</summary>
    const double AccentShare = 0.42;

    static readonly Rgb AccentBase = Rgb.Parse("#0a0e12");
    static readonly Rgb Black = new(0, 0, 0);
    static readonly Rgb White = new(255, 255, 255);

    /// <summary>The tones of <paramref name="felt"/> in the light or dark theme; <paramref name="accent"/> is Windows' accent colour.</summary>
    public static FeltTones For(Felt felt, Rgb accent, bool dark)
    {
        if (felt is Felt.AccentFelt)
        {
            var light = accent.Mix(AccentShare, AccentBase);
            return Of(dark ? light.Times(DarkScale) : light, dark);
        }
        var (lit, dim) = Pair(felt switch
        {
            Felt.PresetFelt preset => Rgb.Parse(preset.Preset.Hex),
            Felt.CustomFelt custom => new Rgb(custom.R, custom.G, custom.B),
            _ => Rgb.Parse(Appearance.Felt.Presets[0].Hex),
        });
        return Of(dark ? dim : lit, dark);
    }

    /// <summary>
    /// The felt colours of the light and dark themes for a chosen colour: kept
    /// within the luminance the board reads at, keeping its hue.
    /// </summary>
    public static (Rgb Light, Rgb Dark) Pair(Rgb chosen)
    {
        var light = ToBytes(Fit(ToLinear(chosen), MinLuma, MaxLuma));
        var dark = ToBytes(Fit(ToLinear(light.Times(DarkScale)), DarkMinLuma, MaxLuma));
        return (light, dark);
    }

    /// <summary>The light and shade over a felt of colour <paramref name="felt"/>.</summary>
    public static FeltTones Of(Rgb felt, bool dark)
    {
        var shadow = felt.Mix(0.12, Black);
        var sheen = felt.Mix(0.25, White);
        var inner = shadow.At(0.45);
        return new(
            felt,
            Wash: sheen.At(dark ? 0.07 : 0.13),
            Vignette: dark ? Black.At(0.36) : shadow.At(0.30),
            InnerShade: inner,
            Crease: inner.Times(0.5),
            TuckShade: dark ? Black.At(0.48) : shadow.At(0.38),
            Unlit: shadow.At(0.60),
            Fade: felt.At(1 - Grain));
    }

    /// <summary>Brought within [min, max] luminance: darkened by scaling, lightened by adding grey.</summary>
    static Rgb Fit(Rgb linear, double min, double max)
    {
        var y = 0.2126 * linear.R + 0.7152 * linear.G + 0.0722 * linear.B;
        var grey = min - y;
        var fitted = y > max ? linear.Times(max / y)
            : y < min ? new Rgb(linear.R + grey, linear.G + grey, linear.B + grey)
            : linear;
        return new(Math.Clamp(fitted.R, 0, 1), Math.Clamp(fitted.G, 0, 1), Math.Clamp(fitted.B, 0, 1));
    }

    static Rgb ToLinear(Rgb c) => new(Contrast.ToLinear(c.R), Contrast.ToLinear(c.G), Contrast.ToLinear(c.B));

    static Rgb ToBytes(Rgb linear) => new(Contrast.FromLinear(linear.R), Contrast.FromLinear(linear.G), Contrast.FromLinear(linear.B));
}
