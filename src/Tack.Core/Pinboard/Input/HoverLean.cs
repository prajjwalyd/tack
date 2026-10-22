using Tack.Core.Geometry;

namespace Tack.Core.Pinboard.Input;

/// <summary>
/// Where the pointer is over the hovered print, and how far the print leans
/// toward it on its pin: its free end swings toward the pointer, more the
/// lower the pointer is. The sheen follows <see cref="Nx"/> and <see cref="Ny"/>.
/// </summary>
/// <param name="Nx">-1 at the print's left edge to 1 at its right, held to that range.</param>
/// <param name="Ny">-1 at its top to 1 at its bottom, held to that range.</param>
/// <param name="Degrees">The lean, clockwise, at most <see cref="MostDegrees"/> either way.</param>
public readonly record struct HoverLean(double Nx, double Ny, double Degrees)
{
    /// <summary>The most a hovered print leans, at a bottom corner.</summary>
    public const double MostDegrees = 0.9;

    /// <summary>The lean for the pointer at (<paramref name="x"/>, <paramref name="y"/>) over a print whose slot is <paramref name="slot"/>, in the same units.</summary>
    public static HoverLean At(LogicalRect slot, double x, double y)
    {
        var nx = Unit((x - slot.X) / slot.W);
        var ny = Unit((y - slot.Y) / slot.H);
        return new HoverLean(nx, ny, -nx * MostDegrees * (0.35 + 0.65 * (ny + 1) / 2));
    }

    /// <summary>0 to 1 across the slot as -1 to 1; an empty slot is its middle.</summary>
    static double Unit(double t) => double.IsFinite(t) ? Math.Clamp(t * 2 - 1, -1, 1) : 0;
}
