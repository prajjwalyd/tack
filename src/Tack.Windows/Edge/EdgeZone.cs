using Tack.Core.Geometry;
using Tack.Windows.Display;

namespace Tack.Windows.Edge;

/// <summary>
/// Where the top edge counts: the middle third of each monitor's top. The
/// corners and the far sides of the edge are where window controls and tab
/// strips are, and must never meet a Tack window.
/// </summary>
internal static class EdgeZone
{
    /// <summary>The part of a monitor's width, centred, that is the zone. The one place to tune it.</summary>
    const double Fraction = 1.0 / 3.0;

    /// <summary>
    /// How far down the zone reaches, physical px. A pointer cannot go above the
    /// screen, so the top row is always met.
    /// </summary>
    public const int Thickness = 2;

    /// <summary>
    /// The zone along the top of <paramref name="monitor"/> (physical px): its
    /// middle third, <paramref name="thickness"/> tall. Each monitor has its own,
    /// however they sit side by side.
    /// </summary>
    public static Rect Of(Rect monitor, int thickness)
    {
        var w = (int)Math.Round(monitor.W * Fraction, MidpointRounding.AwayFromZero);
        return new Rect(monitor.X + (monitor.W - w) / 2, monitor.Y, w, thickness);
    }

    /// <summary>The zone of the monitor holding the point, or the nearest one.</summary>
    public static Rect At(int x, int y) => Of(Monitors.At(x, y).Bounds, Thickness);
}
