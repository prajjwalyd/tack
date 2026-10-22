using Tack.Core.Thumbnails;
using Windows.Graphics.Imaging;

namespace Tack.Windows.Imaging;

/// <summary>The little picture that follows the pointer while a print is dragged out of the board.</summary>
/// <param name="Bgra">Premultiplied BGRA, top-down rows, as Windows wants a drag image.</param>
public sealed record DragImage(int Width, int Height, byte[] Bgra)
{
    /// <summary>Has a size and exactly the bytes that size needs.</summary>
    public bool IsWhole => Width > 0 && Height > 0 && Bgra.LongLength == (long)Width * Height * 4;

    /// <summary>
    /// The drag image of a print, made from its thumbnail (or flight picture),
    /// so it costs next to nothing: scaled to fill a <see cref="ThumbnailRules.DragSide"/>
    /// px box, keeping its shape. Null if the bytes are not a PNG or JPEG.
    /// </summary>
    public static async Task<DragImage?> FromAsync(byte[] picture)
    {
        if (PictureFormat.CodecFor(picture) is not { } codec)
        {
            return null;
        }
        try
        {
            using var memory = new MemoryStream(picture, writable: false);
            using var stream = memory.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(codec, stream).AsTask().ConfigureAwait(false);
            var (width, height) = Filling((int)decoder.PixelWidth, (int)decoder.PixelHeight, ThumbnailRules.DragSide);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height,
                InterpolationMode = BitmapInterpolationMode.Linear,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
            return new DragImage(width, height, pixels.DetachPixelData());
        }
        catch (Exception)
        {
            // A drag works without its picture.
            return null;
        }
    }

    /// <summary>The size that fills a <paramref name="side"/> px box keeping the picture's shape, growing a small one too.</summary>
    static (int Width, int Height) Filling(int width, int height, int side)
    {
        var ratio = Math.Min((double)side / width, (double)side / height);
        return (
            Math.Max((int)Math.Round(width * ratio, MidpointRounding.AwayFromZero), 1),
            Math.Max((int)Math.Round(height * ratio, MidpointRounding.AwayFromZero), 1));
    }
}
