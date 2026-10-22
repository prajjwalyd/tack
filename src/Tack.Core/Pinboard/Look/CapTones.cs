namespace Tack.Core.Pinboard.Look;

/// <summary>A corner cap's tones in one theme.</summary>
/// <param name="Top">Its face at the top of the gradient across it.</param>
/// <param name="Bottom">Its face at the bottom.</param>
/// <param name="Lit">The rim along its inner curve, catching the light.</param>
/// <param name="Shaded">The rim along its outer curve, turned away.</param>
public sealed record CapTones(Rgb Top, Rgb Bottom, Rgb Lit, Rgb Shaded);
