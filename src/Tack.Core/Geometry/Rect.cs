namespace Tack.Core.Geometry;

/// <summary>A rectangle in physical pixels. Contains is half-open: the right and bottom edges are outside.</summary>
public readonly record struct Rect(int X, int Y, int W, int H)
{
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;

    public Rect Inflate(int by) => new(X - by, Y - by, W + 2 * by, H + 2 * by);

    /// <summary>The smallest rectangle holding both.</summary>
    public Rect Union(Rect other)
    {
        var (x, y) = (Math.Min(X, other.X), Math.Min(Y, other.Y));
        var right = Math.Max(X + W, other.X + other.W);
        var bottom = Math.Max(Y + H, other.Y + other.H);
        return new Rect(x, y, right - x, bottom - y);
    }

    /// <summary>The part inside <paramref name="other"/>, if any.</summary>
    public Rect? Intersect(Rect other)
    {
        var (x, y) = (Math.Max(X, other.X), Math.Max(Y, other.Y));
        var right = Math.Min(X + W, other.X + other.W);
        var bottom = Math.Min(Y + H, other.Y + other.H);
        return right > x && bottom > y ? new Rect(x, y, right - x, bottom - y) : null;
    }

    public (int X, int Y) Center => (X + W / 2, Y + H / 2);

    /// <summary>
    /// The same rectangle in logical pixels, relative to a window whose top-left
    /// is at <paramref name="origin"/> (physical) on a monitor scaled by
    /// <paramref name="scale"/>. A scale of 0 or less counts as 1.
    /// </summary>
    public LogicalRect ToLogical((int X, int Y) origin, double scale)
    {
        var s = scale > 0 ? scale : 1.0;
        return new LogicalRect((X - origin.X) / s, (Y - origin.Y) / s, W / s, H / s);
    }
}
