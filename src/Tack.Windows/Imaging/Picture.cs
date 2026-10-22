using Tack.Core.Snips;
using Tack.Core.Thumbnails;

namespace Tack.Windows.Imaging;

/// <summary>
/// A picture in memory: 32-bit BGRA with straight (not premultiplied) alpha,
/// top-down rows, no padding. Decoded files, clipboard captures and screen
/// grabs all end up as one of these; it is never written anywhere unless the
/// caller encodes and saves it.
/// </summary>
public sealed class Picture
{
    /// <summary>Wraps <paramref name="bgra"/>, which must hold exactly <paramref name="width"/> x <paramref name="height"/> pixels; it is not copied.</summary>
    public Picture(int width, int height, byte[] bgra)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (bgra.LongLength != (long)width * height * 4)
        {
            throw new ArgumentException($"A {width} x {height} picture needs {(long)width * height * 4} bytes, not {bgra.Length}.", nameof(bgra));
        }
        (Width, Height, Bgra) = (width, height, bgra);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The pixels, blue, green, red, alpha.</summary>
    public byte[] Bgra { get; }

    /// <summary>The picture's pixel hash: alike for the same pixels whether they came off the clipboard or out of a file.</summary>
    public ulong Hash() => PixelHash.Of(Width, Height, Bgra, PixelLayout.Bgra32);

    /// <summary>Every pixel is fully opaque.</summary>
    public bool IsOpaque()
    {
        var pixels = Bgra.AsSpan();
        for (var at = 3; at < pixels.Length; at += 4)
        {
            if (pixels[at] != 255)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The colour at (x, y), which must lie inside the picture.</summary>
    public Rgb RgbAt(int x, int y)
    {
        var at = ((long)y * Width + x) * 4;
        return new Rgb(Bgra[at + 2], Bgra[at + 1], Bgra[at]);
    }

    /// <summary>
    /// Screenshots have no transparency: an alpha that is the same everywhere
    /// (all opaque, or all clear from a bitmap that never set it) is made
    /// opaque, so the picture neither saves invisible nor carries an alpha
    /// channel it does not need. Changes this picture in place and returns it.
    /// </summary>
    public Picture WithoutFlatAlpha()
    {
        if (PixelOps.HasFlatAlpha(Bgra))
        {
            for (var at = 3; at < Bgra.Length; at += 4)
            {
                Bgra[at] = 255;
            }
        }
        return this;
    }
}
