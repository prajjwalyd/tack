using Windows.Graphics.Imaging;

namespace Tack.Windows.Imaging;

/// <summary>
/// Tells a PNG from a JPEG by its first bytes, and a whole PNG from one still
/// being written by its last. Windows Imaging is never left to guess the
/// format, so no other codec on the machine ever reads a file Tack was handed.
/// </summary>
static class PictureFormat
{
    /// <summary>How many bytes at the end of a file are read to find its end marker.</summary>
    public const int TailLength = 1024;

    static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static ReadOnlySpan<byte> JpegStart => [0xFF, 0xD8, 0xFF];

    /// <summary>The IEND chunk's type and its fixed CRC: the last thing in every whole PNG.</summary>
    static ReadOnlySpan<byte> PngEnd => [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    /// <summary>The decoder for a picture starting with <paramref name="head"/>: PNG or JPEG, else null.</summary>
    public static Guid? CodecFor(ReadOnlySpan<byte> head) =>
        head.StartsWith(PngSignature) ? BitmapDecoder.PngDecoderId
        : head.StartsWith(JpegStart) ? BitmapDecoder.JpegDecoderId
        : null;

    /// <summary>
    /// The codec for a picture with this <paramref name="head"/> and
    /// <paramref name="tail"/> (its last <see cref="TailLength"/> bytes, or
    /// fewer). Throws <see cref="InvalidDataException"/> for anything but a
    /// PNG or JPEG, and for a PNG without its end: Windows Imaging would decode
    /// a half-written screenshot as a half-grey picture rather than fail.
    /// </summary>
    public static Guid Check(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail)
    {
        var codec = CodecFor(head) ?? throw new InvalidDataException("Neither PNG nor JPEG.");
        if (codec == BitmapDecoder.PngDecoderId && tail.IndexOf(PngEnd) < 0)
        {
            throw new InvalidDataException("The PNG is cut short.");
        }
        return codec;
    }

    /// <summary>The last <see cref="TailLength"/> bytes of <paramref name="bytes"/>, or all of them.</summary>
    public static ReadOnlySpan<byte> Tail(ReadOnlySpan<byte> bytes) => bytes[Math.Max(bytes.Length - TailLength, 0)..];
}
