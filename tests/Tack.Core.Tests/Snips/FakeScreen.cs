using Tack.Core.Geometry;
using Tack.Core.Snips;

namespace Tack.Core.Tests.Snips;

/// <summary>A made-up screen: busy, never-repeating content everywhere, unless a patch paints over part of it.</summary>
sealed class FakeScreen : IPixels
{
    public Rect Area { get; init; } = new(0, 0, 2880, 1800);

    public (Rect Where, Func<int, int, Rgb> Paint)? Patch { get; init; }

    public static Rgb Busy(int x, int y)
    {
        var h = unchecked((uint)(x * 73_856_093) ^ (uint)(y * 19_349_663));
        h ^= h >> 13;
        return new Rgb((byte)(h & 0xff), (byte)((h >> 8) & 0xff), (byte)((h >> 16) & 0xff));
    }

    public Rgb? Get(int x, int y)
    {
        if (!Area.Contains(x, y))
        {
            return null;
        }
        if (Patch is { } patch && patch.Where.Contains(x, y))
        {
            return patch.Paint(x, y);
        }
        return Busy(x, y);
    }

    /// <summary>The picture a snip of <paramref name="r"/> would give.</summary>
    public Samples Snip(Rect r) => Samples.Of(r.W, r.H, (x, y) => Get(r.X + x, r.Y + y)!.Value);
}
