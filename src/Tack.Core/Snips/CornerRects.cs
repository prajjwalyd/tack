using Tack.Core.Geometry;

namespace Tack.Core.Snips;

/// <summary>The rectangle a picture fills with the pointer on one of its corners.</summary>
public static class CornerRects
{
    /// <summary>Every corner, likeliest first.</summary>
    public static IReadOnlyList<Corner> All { get; } = [Corner.BottomRight, Corner.TopLeft, Corner.BottomLeft, Corner.TopRight];

    /// <summary>
    /// The <paramref name="w"/> x <paramref name="h"/> rectangle with the pointer on
    /// this corner. The pointer sits on the pixel just past a bottom or right edge,
    /// the way a drag's end point bounds a selection.
    /// </summary>
    public static Rect RectFor(this Corner corner, (int X, int Y) pointer, int w, int h)
    {
        var (px, py) = pointer;
        var (x, y) = corner switch
        {
            Corner.BottomRight => (px - w, py - h),
            Corner.TopLeft => (px, py),
            Corner.BottomLeft => (px, py - h),
            _ => (px - w, py),
        };
        return new Rect(x, y, w, h);
    }
}
