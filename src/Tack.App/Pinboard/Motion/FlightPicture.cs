using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Tack.Core.Diagnostics;
using Windows.Storage.Streams;

namespace Tack.App.Pinboard.Motion;

/// <summary>A flight's picture, decoded off the UI thread at the size it first shows.</summary>
static class FlightPicture
{
    /// <summary>The picture's long side, as the app sends it.</summary>
    const int Largest = 1600;

    /// <summary>
    /// <paramref name="jpeg"/> decoded about <paramref name="pixelWidth"/>
    /// physical px wide (never wider than it was sent); null if it cannot be.
    /// </summary>
    public static async Task<BitmapImage?> LoadAsync(byte[] jpeg, int pixelWidth)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(jpeg.AsBuffer());
            stream.Seek(0);
            var picture = new BitmapImage { DecodePixelType = DecodePixelType.Physical };
            if (pixelWidth is > 0 and < Largest)
            {
                picture.DecodePixelWidth = pixelWidth;
            }
            await picture.SetSourceAsync(stream);
            return picture;
        }
        catch (Exception e)
        {
            DevTrace.Write($"flight: the picture could not be shown: {e.Message}");
            return null;
        }
    }
}
