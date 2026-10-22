namespace Tack.Core.Thumbnails;

/// <summary>Small operations on 4-byte pixels (the alpha last), in RGBA or BGRA order alike.</summary>
public static class PixelOps
{
    /// <summary>JPEG has no alpha: transparent parts go on white paper, not black. Every pixel ends opaque.</summary>
    public static void FlattenOnWhite(Span<byte> pixels)
    {
        for (var at = 0; at + 4 <= pixels.Length; at += 4)
        {
            int alpha = pixels[at + 3];
            for (var c = 0; c < 3; c++)
            {
                pixels[at + c] = (byte)((pixels[at + c] * alpha + 255 * (255 - alpha)) / 255);
            }
            pixels[at + 3] = 255;
        }
    }

    /// <summary>Premultiplies each colour by its alpha, as Windows wants a drag image.</summary>
    public static void Premultiply(Span<byte> pixels)
    {
        for (var at = 0; at + 4 <= pixels.Length; at += 4)
        {
            int alpha = pixels[at + 3];
            for (var c = 0; c < 3; c++)
            {
                pixels[at + c] = (byte)(pixels[at + c] * alpha / 255);
            }
        }
    }

    /// <summary>
    /// Whether every alpha is 255, or every alpha is 0. Screenshots have no
    /// transparency: an all-opaque alpha can go (a smaller PNG), and an all-zero
    /// one (a bitmap that never set it) would otherwise save an invisible picture.
    /// </summary>
    public static bool HasFlatAlpha(ReadOnlySpan<byte> pixels)
    {
        var (opaque, clear) = (true, true);
        for (var at = 3; at < pixels.Length; at += 4)
        {
            opaque &= pixels[at] == 255;
            clear &= pixels[at] == 0;
            if (!opaque && !clear)
            {
                return false;
            }
        }
        return true;
    }
}
