using Tack.Core.Thumbnails;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace Tack.Windows.Imaging;

/// <summary>
/// Encodes pictures with Windows Imaging: PNG for a capture's file, JPEG for
/// the pictures on the board. Encoding failures throw
/// <see cref="InvalidDataException"/>. Safe on any thread; the work runs on
/// the thread pool.
/// </summary>
public static class PictureEncoder
{
    /// <summary>
    /// The picture as a PNG, the way a capture is saved. Without transparency
    /// it is saved without an alpha channel, which makes a smaller file.
    /// </summary>
    public static Task<byte[]> PngAsync(Picture picture) =>
        EncodeAsync(
            BitmapEncoder.PngEncoderId,
            null,
            picture.Bgra,
            picture.IsOpaque() ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Straight,
            (picture.Width, picture.Height),
            (picture.Width, picture.Height));

    /// <summary>
    /// The picture as a JPEG of <paramref name="quality"/> (0 to 100), shrunk
    /// so its long side is at most <paramref name="side"/> px. JPEG has no
    /// alpha: transparent parts go on white paper (in a copy; the picture is
    /// left alone).
    /// </summary>
    public static Task<byte[]> JpegAsync(Picture picture, int side, int quality)
    {
        var pixels = picture.Bgra;
        if (!picture.IsOpaque())
        {
            pixels = (byte[])pixels.Clone();
            PixelOps.FlattenOnWhite(pixels);
        }
        var options = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(quality / 100f, PropertyType.Single),
        };
        return EncodeAsync(
            BitmapEncoder.JpegEncoderId,
            options,
            pixels,
            BitmapAlphaMode.Ignore,
            (picture.Width, picture.Height),
            ThumbnailRules.Fit(picture.Width, picture.Height, side));
    }

    static async Task<byte[]> EncodeAsync(
        Guid encoderId,
        BitmapPropertySet? options,
        byte[] bgra,
        BitmapAlphaMode alpha,
        (int Width, int Height) size,
        (int Width, int Height) scaled)
    {
        try
        {
            using var memory = new MemoryStream();
            using (var stream = memory.AsRandomAccessStream())
            {
                var creating = options is null
                    ? BitmapEncoder.CreateAsync(encoderId, stream)
                    : BitmapEncoder.CreateAsync(encoderId, stream, options);
                var encoder = await creating.AsTask().ConfigureAwait(false);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha, (uint)size.Width, (uint)size.Height, 96, 96, bgra);
                if (scaled != size)
                {
                    // Fant averages every source pixel, as a thumbnail should, rather than skipping some.
                    encoder.BitmapTransform.ScaledWidth = (uint)scaled.Width;
                    encoder.BitmapTransform.ScaledHeight = (uint)scaled.Height;
                    encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                }
                await encoder.FlushAsync().AsTask().ConfigureAwait(false);
            }
            return memory.ToArray();
        }
        catch (Exception e)
        {
            throw new InvalidDataException($"The picture cannot be encoded: {e.Message}", e);
        }
    }
}
