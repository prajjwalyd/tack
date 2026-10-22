namespace Tack.Core.Thumbnails;

/// <summary>
/// The sizes and limits of the pictures Tack makes from a screenshot. The
/// decoding and encoding themselves are the Windows layer's (Windows Imaging).
/// </summary>
public static class ThumbnailRules
{
    /// <summary>Long side of a print on the board, px, and its JPEG quality.</summary>
    public const int ThumbSide = 360;
    public const int ThumbQuality = 85;

    /// <summary>Long side of the drag image that follows the pointer.</summary>
    public const int DragSide = 160;

    /// <summary>Long side of the picture a new capture flies onto the board with: about the snip itself on most screens.</summary>
    public const int FlightSide = 1600;
    public const int FlightQuality = 80;

    /// <summary>
    /// The most any one picture may claim: 16384 px a side and 256 MiB of pixels.
    /// A file that claims more (a tiny PNG can claim gigabytes) is refused.
    /// </summary>
    public const int MaxSide = 16_384;
    public const long MaxBytes = 256L * 1024 * 1024;

    /// <summary>Decodes at once, however many pictures arrive together.</summary>
    public const int MaxDecodes = 2;

    /// <summary>
    /// The size a <paramref name="width"/> x <paramref name="height"/> picture shrinks
    /// to so its long side is at most <paramref name="side"/>, keeping its shape and
    /// never below 1 px. A picture already small enough keeps its size.
    /// </summary>
    public static (int Width, int Height) Fit(int width, int height, int side)
    {
        if (Math.Max(width, height) <= side)
        {
            return (width, height);
        }
        var ratio = Math.Min((double)side / width, (double)side / height);
        return (
            Math.Max((int)Math.Round(width * ratio, MidpointRounding.AwayFromZero), 1),
            Math.Max((int)Math.Round(height * ratio, MidpointRounding.AwayFromZero), 1));
    }
}
