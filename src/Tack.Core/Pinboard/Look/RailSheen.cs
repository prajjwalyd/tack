namespace Tack.Core.Pinboard.Look;

/// <summary>
/// The brushed sheen laid along the bottom rail, the same for every finish:
/// black at the shaded ends, then three bands of white light with clear gaps
/// between them, as opacities.
/// </summary>
/// <param name="Ends">The black at both ends.</param>
/// <param name="Bands">The three white bands, left to right.</param>
public sealed record RailSheen(double Ends, IReadOnlyList<double> Bands)
{
    public static RailSheen Light { get; } = new(0.08, [0.18, 0.30, 0.12]);

    public static RailSheen Dark { get; } = new(0.14, [0.14, 0.18, 0.06]);

    /// <summary>Where the bands are brightest, as fractions of the rail's length.</summary>
    public static IReadOnlyList<double> BandsAt { get; } = [0.18, 0.52, 0.86];

    /// <summary>Where the light is clear between the bands.</summary>
    public static IReadOnlyList<double> GapsAt { get; } = [0.34, 0.70];

    public static RailSheen In(bool dark) => dark ? Dark : Light;
}
