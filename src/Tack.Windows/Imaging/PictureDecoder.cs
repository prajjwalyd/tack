using Tack.Core.Thumbnails;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Tack.Windows.Imaging;

/// <summary>
/// Decodes PNG and JPEG pictures, and bitmaps off the clipboard, with Windows
/// Imaging. Each picture is held to <see cref="ThumbnailRules"/>' limits
/// before its pixels are decoded (a tiny PNG can claim gigabytes), and at most
/// <see cref="ThumbnailRules.MaxDecodes"/> decode at once, however many
/// arrive together. Anything that cannot be decoded throws
/// <see cref="InvalidDataException"/>. Safe on any thread; the work runs on
/// the thread pool.
/// </summary>
public static class PictureDecoder
{
    static readonly SemaphoreSlim s_turns = new(ThumbnailRules.MaxDecodes);

    /// <summary>
    /// Decodes a PNG or JPEG file. Opened the way other programs expect, so a
    /// screenshot still being written or moved is not held up; a file that
    /// cannot be opened throws <see cref="IOException"/>.
    /// </summary>
    public static async Task<Picture> DecodeFileAsync(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using (file.ConfigureAwait(false))
        {
            var head = new byte[8];
            var headRead = await file.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false).ConfigureAwait(false);
            var tail = new byte[(int)Math.Min(file.Length, PictureFormat.TailLength)];
            file.Position = file.Length - tail.Length;
            var tailRead = await file.ReadAtLeastAsync(tail, tail.Length, throwOnEndOfStream: false).ConfigureAwait(false);
            var codec = PictureFormat.Check(head.AsSpan(0, headRead), tail.AsSpan(0, tailRead));
            file.Position = 0;
            using var stream = file.AsRandomAccessStream();
            return await DecodeAsync(stream, codec).ConfigureAwait(false);
        }
    }

    /// <summary>Decodes a PNG or JPEG already in memory (a drop, a phone pin, the clipboard's PNG).</summary>
    public static async Task<Picture> DecodeAsync(byte[] bytes)
    {
        var codec = PictureFormat.Check(bytes, PictureFormat.Tail(bytes));
        using var memory = new MemoryStream(bytes, writable: false);
        using var stream = memory.AsRandomAccessStream();
        return await DecodeAsync(stream, codec).ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes the clipboard's CF_DIB bitmap. As for every capture, an alpha
    /// that is the same everywhere is made opaque (see <see cref="Picture.WithoutFlatAlpha"/>).
    /// </summary>
    public static async Task<Picture> DecodeDibAsync(byte[] dib)
    {
        using var memory = new MemoryStream(BitmapFile.For(dib), writable: false);
        using var stream = memory.AsRandomAccessStream();
        var picture = await DecodeAsync(stream, BitmapDecoder.BmpDecoderId).ConfigureAwait(false);
        return picture.WithoutFlatAlpha();
    }

    static async Task<Picture> DecodeAsync(IRandomAccessStream stream, Guid codec)
    {
        try
        {
            var decoder = await BitmapDecoder.CreateAsync(codec, stream).AsTask().ConfigureAwait(false);
            var (width, height) = (decoder.PixelWidth, decoder.PixelHeight);
            if (width == 0 || height == 0 || width > ThumbnailRules.MaxSide || height > ThumbnailRules.MaxSide
                || (long)width * height * 4 > ThumbnailRules.MaxBytes)
            {
                throw new InvalidDataException($"A {width} x {height} picture is more than Tack takes.");
            }
            await s_turns.WaitAsync().ConfigureAwait(false);
            try
            {
                // As stored: no colour management, and no turning by the EXIF orientation.
                var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
                return new Picture((int)width, (int)height, pixels.DetachPixelData());
            }
            finally
            {
                s_turns.Release();
            }
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            // Windows Imaging reports a bad file in many ways; to Tack they all mean "not a picture".
            throw new InvalidDataException($"The picture cannot be decoded: {e.Message}", e);
        }
    }
}
