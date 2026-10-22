using System.Diagnostics;
using Tack.Core.Thumbnails;

namespace Tack.Windows.Imaging;

/// <summary>
/// The prints on the board: a small JPEG of each picture, with the original's
/// size and pixel hash. Safe on any thread; decoding and encoding run on the
/// thread pool.
/// </summary>
public static class Thumbs
{
    /// <summary>How long to wait between tries at a screenshot that is still being written.</summary>
    static readonly TimeSpan s_retryGap = TimeSpan.FromMilliseconds(150);

    /// <summary>The thumbnail of a PNG or JPEG file. Throws <see cref="IOException"/> or <see cref="InvalidDataException"/>.</summary>
    public static async Task<Thumb> MakeAsync(string path)
    {
        var picture = await PictureDecoder.DecodeFileAsync(path).ConfigureAwait(false);
        return await FromPictureAsync(picture).ConfigureAwait(false);
    }

    /// <summary>The thumbnail of a picture already in memory, such as a dropped one.</summary>
    public static async Task<Thumb> FromPictureAsync(Picture picture)
    {
        var jpeg = await PictureEncoder.JpegAsync(picture, ThumbnailRules.ThumbSide, ThumbnailRules.ThumbQuality).ConfigureAwait(false);
        return new Thumb(jpeg, picture.Width, picture.Height, picture.Hash());
    }

    /// <summary>
    /// For a capture that flies onto the board: the picture it flies with and
    /// its thumbnail. <paramref name="hash"/> is the picture's own, already
    /// taken to spot a repeat.
    /// </summary>
    public static async Task<FlightPicture> ForFlightAsync(Picture picture, ulong hash)
    {
        var flight = await PictureEncoder.JpegAsync(picture, ThumbnailRules.FlightSide, ThumbnailRules.FlightQuality).ConfigureAwait(false);
        var thumb = await PictureEncoder.JpegAsync(picture, ThumbnailRules.ThumbSide, ThumbnailRules.ThumbQuality).ConfigureAwait(false);
        return new FlightPicture(flight, new Thumb(thumb, picture.Width, picture.Height, hash));
    }

    /// <summary>
    /// As <see cref="MakeAsync"/>, for a fresh screenshot: Snipping Tool creates
    /// the file first and writes it a moment later, so it is tried again for up
    /// to <paramref name="patience"/>, unless the file is gone.
    /// </summary>
    public static async Task<Thumb> MakePatientlyAsync(string path, TimeSpan patience)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return await MakeAsync(path).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
                && Stopwatch.GetElapsedTime(started) < patience && File.Exists(path))
            {
                // Not written yet: try again shortly.
            }
            await Task.Delay(s_retryGap).ConfigureAwait(false);
        }
    }
}
