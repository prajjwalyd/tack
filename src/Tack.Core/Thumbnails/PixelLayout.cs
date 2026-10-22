namespace Tack.Core.Thumbnails;

/// <summary>How a picture's pixels are laid out in memory, one byte per channel.</summary>
public enum PixelLayout
{
    /// <summary>Red, green, blue.</summary>
    Rgb24,

    /// <summary>Red, green, blue, alpha.</summary>
    Rgba32,

    /// <summary>Blue, green, red, alpha: what Windows decoders hand back.</summary>
    Bgra32,
}
