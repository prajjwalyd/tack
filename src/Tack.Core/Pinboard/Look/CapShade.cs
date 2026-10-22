namespace Tack.Core.Pinboard.Look;

/// <summary>What a corner cap casts on the felt, the same for every finish.</summary>
/// <param name="Colour">The shade along its inner curve.</param>
/// <param name="Opacity">How strong that shade is.</param>
/// <param name="Edge">The black hairline along its two flat edges, as an opacity.</param>
public sealed record CapShade(Rgb Colour, double Opacity, double Edge)
{
    public static CapShade Light { get; } = new(Rgb.Parse("#001a0c"), 0.45, 0.2);

    public static CapShade Dark { get; } = new(new Rgb(0, 0, 0), 0.55, 0.28);

    public static CapShade In(bool dark) => dark ? Dark : Light;
}
