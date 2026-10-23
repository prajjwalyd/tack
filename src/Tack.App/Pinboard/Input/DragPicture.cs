using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Tack.Core.Thumbnails;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// The picture that follows the pointer while a print's file is dragged out:
/// its thumbnail, filling a box of <see cref="Side"/> px and held by its centre,
/// as in Tack 0.2. A note has none and is drawn as the card itself.
/// </summary>
static class DragPicture
{
    /// <summary>The box the picture fills, physical px, a small picture growing to it too.</summary>
    const int Side = 160;

    /// <summary>Puts <paramref name="thumb"/>'s picture under the pointer for the drag starting.</summary>
    public static async Task ShowAsync(DragUI ui, Thumb thumb, double scale)
    {
        var wide = thumb.Width >= thumb.Height;
        (double W, double H) size = wide
            ? (Side, Math.Max(1, Math.Round(Side * (double)thumb.Height / thumb.Width)))
            : (Math.Max(1, Math.Round(Side * (double)thumb.Width / thumb.Height)), Side);
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(thumb.Jpeg.AsBuffer());
        stream.Seek(0);
        var picture = new BitmapImage { DecodePixelType = DecodePixelType.Physical };
        if (wide)
        {
            picture.DecodePixelWidth = Side;
        }
        else
        {
            picture.DecodePixelHeight = Side;
        }
        await picture.SetSourceAsync(stream);
        // The anchor is in view pixels.
        ui.SetContentFromBitmapImage(picture, new Point(size.W / 2 / scale, size.H / 2 / scale));
    }
}
