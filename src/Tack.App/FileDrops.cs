using Tack.App.Pinboard.Contract;
using Tack.Core.Diagnostics;
using Tack.Core.Files;
using Tack.Core.Prints;
using Tack.Core.Thumbnails;
using Tack.Windows.Shell;

namespace Tack.App;

/// <summary>
/// Files and folders dropped on the board that are not pictures to read: each
/// is pinned where it is, never copied, with the thumbnail Explorer would
/// show and its name. The user's file stays theirs: Tack never moves or
/// recycles it. One that is moved or deleted leaves the board quietly the
/// next time the board comes down (<see cref="CheckGone"/>).
/// </summary>
sealed class FileDrops(AppState state, Prints prints, Unpinning unpinning, Reveal reveal, Notices notices)
{
    /// <summary>Pins the files at <paramref name="paths"/>, in the order dropped; on the thread pool, since the shell draws each one.</summary>
    public void Pin(IReadOnlyList<string> paths) => Background.Run("pinning dropped files", () => PinAsync(paths));

    /// <summary>A file's face and facts, as pinned now or put back at startup. Throws <see cref="IOException"/> for one that is not there.</summary>
    public static async Task<(Thumb? Thumb, FileFacts Facts)> ReadAsync(string path)
    {
        var folder = Directory.Exists(path);
        var size = folder ? 0 : new FileInfo(path).Length;
        var facts = new FileFacts(folder, size, ShellTypes.NameOf(path, folder));
        return (await ShellThumbnail.MakeAsync(path).ConfigureAwait(false), facts);
    }

    /// <summary>
    /// Unpins, with a quiet fade, every file print whose file or folder was
    /// moved or deleted. As the board comes down, on the thread pool.
    /// </summary>
    public void CheckGone()
    {
        List<string> linked;
        lock (state.Gate)
        {
            linked = [.. state.Board.Prints.Where(p => p.IsFile).Select(p => p.Path)];
        }
        foreach (var path in linked.Where(LinkedFile.IsGone))
        {
            DevTrace.Write($"files: {Path.GetFileName(path)} was moved or deleted; unpinning it");
            unpinning.RemovePath(path, Removal.Quiet);
        }
    }

    async Task PinAsync(IReadOnlyList<string> paths)
    {
        // Drawn side by side (the shell limits how many at once), pinned in the order dropped.
        var reads = paths.Select(path => (Path: path, Read: TryReadAsync(path))).ToList();
        var (pinned, already) = (0, 0);
        foreach (var (path, read) in reads)
        {
            if (await read.ConfigureAwait(false) is not { } file)
            {
                continue;
            }
            switch (prints.PinFile(path, file.Thumb, file.Facts, Prints.Live()))
            {
                case PinOutcome.New added:
                    // The board comes down once, for the first; the rest arrive on it.
                    if (pinned++ == 0)
                    {
                        await reveal.ForNewAsync().ConfigureAwait(false);
                    }
                    prints.AnnounceLater(added.Aged);
                    break;
                case PinOutcome.Exists:
                    already++;
                    break;
            }
        }
        if (pinned == 0)
        {
            notices.Show(already > 0 ? Notices.AlreadyPinned : Notices.Failed);
        }
        DevTrace.Write($"files: {pinned} of {paths.Count} pinned, {already} already");
    }

    static async Task<(Thumb? Thumb, FileFacts Facts)?> TryReadAsync(string path)
    {
        try
        {
            return await ReadAsync(path).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DevTrace.Write($"files: cannot pin {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }
}
