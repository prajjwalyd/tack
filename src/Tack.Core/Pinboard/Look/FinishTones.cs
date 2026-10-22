namespace Tack.Core.Pinboard.Look;

/// <summary>
/// A frame finish's tones in one theme. The frame is lit from above: the
/// bottom rail's top face is a bright hairline and each side rail is
/// brightest at its inner edge.
/// </summary>
/// <param name="Bottom">
/// The bottom rail from its top face down: the hairline (the lit edge), two
/// bevel steps, the face, and two to the shaded edge. Six tones.
/// </param>
/// <param name="Side">
/// A side rail from its outer edge to its inner one: the shaded outer edge, a
/// bevel, the face, a bevel, the lit inner edge. Five tones.
/// </param>
/// <param name="Cap">The corner caps.</param>
public sealed record FinishTones(IReadOnlyList<Rgb> Bottom, IReadOnlyList<Rgb> Side, CapTones Cap)
{
    /// <summary>Tones from "#rrggbb" text: six bottom, five side, then the cap's top, bottom, lit and shaded.</summary>
    public static FinishTones Parse(string[] bottom, string[] side, string[] cap) =>
        new(
            [.. bottom.Select(Rgb.Parse)],
            [.. side.Select(Rgb.Parse)],
            new CapTones(Rgb.Parse(cap[0]), Rgb.Parse(cap[1]), Rgb.Parse(cap[2]), Rgb.Parse(cap[3])));
}
