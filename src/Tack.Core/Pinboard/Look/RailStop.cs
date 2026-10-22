namespace Tack.Core.Pinboard.Look;

/// <summary>Where a tone sits across a rail.</summary>
/// <param name="At">Logical px from the rail's first edge (the bottom rail's top, a side rail's outer edge).</param>
/// <param name="Tone">Which of the rail's tones, by index.</param>
public readonly record struct RailStop(double At, int Tone);
