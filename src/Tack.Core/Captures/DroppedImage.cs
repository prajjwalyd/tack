namespace Tack.Core.Captures;

/// <summary>
/// The rules for a picture dropped on the board: a PNG or JPEG by its first
/// bytes, whatever its name says, at most 40 MB. It is saved unchanged among
/// the captures, as "Image YYYY-MM-DD HHMMSS.png" (or ".jpg").
/// </summary>
public static class DroppedImage
{
    /// <summary>The largest picture accepted from a drop.</summary>
    public const int MaxBytes = 40 * 1024 * 1024;

    /// <summary>The start of a dropped picture's file name.</summary>
    public const string Stem = "Image";

    static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static ReadOnlySpan<byte> JpegStart => [0xFF, 0xD8, 0xFF];

    /// <summary>Too large to be a picture Tack takes.</summary>
    public static bool TooLarge(long length) => length > MaxBytes;

    /// <summary>The extension to save the picture under: "png", "jpg", or null for anything else.</summary>
    public static string? Extension(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(PngSignature) ? "png"
        : bytes.StartsWith(JpegStart) ? "jpg"
        : null;
}
