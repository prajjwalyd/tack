using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Prints;
using Tack.Core.Thumbnails;
using Tack.Windows.Imaging;

namespace Tack.App;

/// <summary>
/// A picture dropped on the board, or sent from the phone board: the bytes of
/// a PNG or JPEG file, at most 40 MB (<see cref="DroppedImage"/>). Saved
/// unchanged in Tack's Captures folder as "Image ..." and pinned like a
/// capture, so it goes to the Recycle Bin when it leaves the board. Anything
/// else gets a notice instead.
/// </summary>
sealed class ImageDrops(Prints prints, OwnedFiles files, Reveal reveal, Notices notices, string capturesFolder)
{
    /// <summary>Pins the dropped picture; on the thread pool, since it decodes and writes a file.</summary>
    public void Pin(string name, byte[] bytes) => Background.Run("pinning a dropped picture", () => PinAsync(name, bytes));

    /// <summary>Pins the picture where the caller is; true if it is on the board now. What happened goes to the trace.</summary>
    public async Task<bool> PinAsync(string name, byte[] bytes)
    {
        var (pinned, what) = await TryPinAsync(name, bytes).ConfigureAwait(false);
        DevTrace.Write($"pinned picture: {what}");
        return pinned;
    }

    /// <summary>Whether it was pinned, and what happened, for the trace.</summary>
    async Task<(bool Pinned, string What)> TryPinAsync(string name, byte[] bytes)
    {
        if (DroppedImage.TooLarge(bytes.LongLength))
        {
            notices.Show(Notices.BadImage);
            return (false, $"{name}: too large");
        }
        if (DroppedImage.Extension(bytes) is not { } extension)
        {
            notices.Show(Notices.BadImage);
            return (false, $"{name}: neither PNG nor JPEG");
        }
        Thumb thumb;
        try
        {
            thumb = await Thumbs.FromPictureAsync(await PictureDecoder.DecodeAsync(bytes).ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (InvalidDataException e)
        {
            notices.Show(Notices.BadImage);
            return (false, $"{name}: cannot decode ({e.Message})");
        }
        string path;
        try
        {
            path = ReservedFiles.SaveBytes(bytes, capturesFolder, DroppedImage.Stem, extension, Timestamp.Of(DateTime.Now));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            notices.Show(Notices.Failed);
            return (false, $"{name}: cannot save ({e.Message})");
        }
        if (prints.Pin(path, thumb, Origin.Capture, Prints.Live()) is PinOutcome.New added)
        {
            await reveal.ForNewAsync().ConfigureAwait(false);
            prints.AnnounceLater(added.Aged);
            return (true, $"{name}: pinned");
        }
        files.Discard([path]);
        return (false, $"{name}: not pinned");
    }
}
