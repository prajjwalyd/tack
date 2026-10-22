namespace Tack.Core.Thumbnails;

/// <summary>
/// A hash of a picture's size and its pixels' colours, alpha left out, so the
/// same picture hashes alike off the clipboard (often with alpha) and out of a
/// PNG (often without). FxHash-style, a pixel at a time: it only has to tell
/// pictures apart within a session, and is never saved.
/// </summary>
public static class PixelHash
{
    public static ulong Of(int width, int height, ReadOnlySpan<byte> pixels, PixelLayout layout)
    {
        var hash = Mix(Mix(0, (uint)width), (uint)height);
        var size = layout == PixelLayout.Rgb24 ? 3 : 4;
        for (var at = 0; at + size <= pixels.Length; at += size)
        {
            var (r, g, b) = layout == PixelLayout.Bgra32
                ? (pixels[at + 2], pixels[at + 1], pixels[at])
                : (pixels[at], pixels[at + 1], pixels[at + 2]);
            hash = Mix(hash, r | (uint)g << 8 | (uint)b << 16);
        }
        return hash;
    }

    /// <summary>One FxHash step.</summary>
    static ulong Mix(ulong hash, uint word) => unchecked((ulong.RotateLeft(hash, 5) ^ word) * 0x517c_c1b7_2722_0a95);
}
