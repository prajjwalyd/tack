using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Files;
using Tack.Windows.Imaging;

namespace Tack.App;

/// <summary>
/// Writing a capture's PNG in the background. Encoding can take most of a
/// second, so the file's name is claimed at once and the print is pinned under
/// it while the picture is written; anything that needs the file itself waits
/// through <see cref="PendingSaves"/>.
/// </summary>
sealed class CaptureSaving(AppState state, Prints prints, PendingSaves saves, string folder)
{
    /// <summary>
    /// Claims the capture's file name in the Captures folder and writes the
    /// picture there in the background. Returns the path it will have. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when no name can be claimed.
    /// </summary>
    public string Save(Picture picture)
    {
        var path = ReservedFiles.Reserve(folder, Timestamp.Of(DateTime.Now));
        saves.Begin(path);
        Background.Run("saving a capture", () => WriteAsync(path, picture));
        return path;
    }

    /// <summary>Whether <paramref name="path"/> lies directly in the Captures folder.</summary>
    public bool InCaptures(string path) =>
        Path.GetDirectoryName(path) is { } parent && PathRules.Key(parent) == PathRules.Key(folder);

    async Task WriteAsync(string path, Picture picture)
    {
        try
        {
            var png = await PictureEncoder.PngAsync(picture).ConfigureAwait(false);
            ReservedFiles.WriteReserved(png, path);
            // From now on only an edit of the file counts as a change.
            lock (state.Gate)
            {
                state.Board.RefreshStamp(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DevTrace.Write($"cannot save the capture {Path.GetFileName(path)}: {e.Message}");
            RemovePartial(path);
            prints.RemovePath(path);
        }
        finally
        {
            saves.Finish(path);
        }
    }

    /// <summary>Removes the empty ".part" file claimed for a capture that could not be encoded.</summary>
    static void RemovePartial(string path)
    {
        try
        {
            File.Delete(ReservedFiles.PartialPath(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the startup sweep, which takes stray ".part" files.
        }
    }
}
