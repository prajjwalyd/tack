using Tack.Core.Geometry;
using Tack.Core.Snips;

namespace Tack.Windows.Capture;

/// <summary>
/// A grab of the screen, read by absolute physical position: 32-bit BGRA,
/// top-down rows, in memory that lives only as long as the
/// <see cref="ScreenCopy.With"/> callback it is handed to.
/// </summary>
sealed unsafe class ScreenPixels : IPixels
{
    readonly Rect _area;
    readonly byte* _bits;

    /// <summary>Reads <paramref name="bits"/>, which hold <paramref name="area"/>'s width x height pixels.</summary>
    public ScreenPixels(Rect area, byte* bits)
    {
        _area = area;
        _bits = bits;
    }

    public Rgb? Get(int x, int y)
    {
        if (!_area.Contains(x, y))
        {
            return null;
        }
        var at = (((long)y - _area.Y) * _area.W + (x - _area.X)) * 4;
        return new Rgb(_bits[at + 2], _bits[at + 1], _bits[at]);
    }
}
