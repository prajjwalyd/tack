using Tack.Core.Pinboard.Look;

namespace Tack.Core.Appearance;

/// <summary>
/// The picture of a frame finish on the Appearance card: a round piece of the
/// metal, lit from above, drawn from the bottom rail's own tones so the swatch
/// is the metal the board's rails will be and no colour is written down twice.
/// The rail's tones run down it as a gradient (the lit edge at the top, the
/// shaded one at the bottom), with a soft highlight at the top left and faint
/// bands turning round it for the brushed grain.
/// </summary>
/// <param name="Metal">The gradient from top to bottom: the rail's six tones.</param>
/// <param name="Shine">How opaque the white highlight at the top left is at its brightest.</param>
/// <param name="Grain">The brushed bands, white at these opacities, turning clockwise from <see cref="GrainFrom"/>.</param>
/// <param name="Ink">The check mark: white or near-black, whichever reads better on the face of the metal.</param>
/// <param name="Glow">A faint halo round the check mark in the opposite tone, so it holds on any part of the metal.</param>
public sealed record FinishSwatch(
    IReadOnlyList<SwatchStop> Metal, double Shine, IReadOnlyList<SwatchStop> Grain, Rgb Ink, Rgba Glow)
{
    /// <summary>Where the grain's bands start turning, in degrees clockwise from the top.</summary>
    public const double GrainFrom = 20;

    /// <summary>The light check mark.</summary>
    public static Rgb Light { get; } = new(255, 255, 255);

    /// <summary>The dark check mark.</summary>
    public static Rgb Dark { get; } = Rgb.Parse("#1b1b1b");

    /// <summary>Where each of the bottom rail's tones sits down the swatch.</summary>
    static readonly double[] Stops = [0, 0.12, 0.30, 0.56, 0.84, 1];

    /// <summary>Which of the bottom rail's tones is its face.</summary>
    const int Face = 3;

    /// <summary>The grain's three band strengths, light theme and dark.</summary>
    static readonly double[] LightGrain = [0.34, 0.22, 0.3];
    static readonly double[] DarkGrain = [0.2, 0.12, 0.18];

    /// <summary>The swatch of <paramref name="frame"/> in the light or dark theme.</summary>
    public static FinishSwatch For(Frame frame, bool dark)
    {
        var tones = FrameFinishes.For(frame).In(dark).Bottom;
        var ink = InkOn(tones[Face]);
        return new(
            [.. Stops.Select((at, n) => new SwatchStop(at, tones[n].At(1)))],
            dark ? 0.3 : 0.5,
            GrainOf(dark ? DarkGrain : LightGrain),
            ink,
            ink == Light ? new Rgba(0, 0, 0, 0.55) : new Rgba(255, 255, 255, 0.6));
    }

    /// <summary>The check mark's colour on <paramref name="face"/>: white or near-black, whichever has the more contrast.</summary>
    public static Rgb InkOn(Rgb face) => Contrast.Ratio(Light, face) >= Contrast.Ratio(Dark, face) ? Light : Dark;

    static SwatchStop[] GrainOf(double[] band)
    {
        SwatchStop Stop(double at, double alpha) => new(at, Light.At(alpha));
        return
        [
            Stop(0, 0), Stop(0.09, band[0]), Stop(0.20, 0), Stop(0.38, band[1]), Stop(0.52, 0),
            Stop(0.70, band[2]), Stop(0.84, 0), Stop(0.94, band[0]), Stop(1, 0),
        ];
    }
}
