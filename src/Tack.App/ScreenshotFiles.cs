using Tack.App.Pinboard.Contract;
using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Files;
using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Screenshots;
using Tack.Core.Thumbnails;
using Tack.Windows.Imaging;
using Tack.Windows.Screenshots;

namespace Tack.App;

/// <summary>
/// Screenshot files: what a change in the Screenshots folder means for the
/// board (see <see cref="FolderEvents"/>). New image files get pinned and the
/// board peeks out; an edit to a pinned file redraws its print; a pinned file
/// deleted or moved elsewhere is unpinned with a short fade. Tack's own files
/// have no watch: each reveal looks them over instead (<see cref="CheckOwned"/>).
/// The watch reports on the thread pool; each change is judged under the
/// app's lock and the slow part (decoding, a grace period) runs on as
/// background work.
/// </summary>
sealed class ScreenshotFiles(AppState state, Prints prints, Unpinning unpinning, PendingSaves saves, Reveal reveal) : IDisposable
{
    FolderWatch? _watch;

    /// <summary>Watches <paramref name="folder"/> until disposed. A folder that cannot be watched is traced and left.</summary>
    public void Watch(string folder)
    {
        try
        {
            _watch = new FolderWatch(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            DevTrace.Write($"screenshots: cannot watch {folder}: {e.Message}");
            return;
        }
        _watch.Changed += OnChanged;
        _watch.Ended += () => DevTrace.Write($"screenshots: {folder} went away; no longer watching it");
    }

    /// <summary>
    /// Looks Tack's own files over, as the board comes down (on the thread
    /// pool): a pinned capture or note edited or deleted elsewhere is treated
    /// as the watch would treat a screenshot file. A capture still being
    /// written is left alone.
    /// </summary>
    public void CheckOwned()
    {
        List<(string Path, FileStamp? Stamp)> owned;
        lock (state.Gate)
        {
            owned = [.. state.Board.Prints.Where(p => p.Owned).Select(p => (p.Path, p.Stamp))];
        }
        foreach (var (path, old) in owned)
        {
            if (saves.IsSaving(path))
            {
                continue;
            }
            var now = FileStamp.Of(path);
            FileStep step;
            lock (state.Gate)
            {
                step = state.Files.Owned(state.Board, path, old, now);
            }
            Take(step, path);
        }
    }

    void OnChanged(FolderChange change, string path)
    {
        FileStep step;
        lock (state.Gate)
        {
            step = change switch
            {
                FolderChange.Arrived => state.Files.Arrived(state.Board, path),
                FolderChange.Gone => state.Files.Gone(state.Board, path),
                _ => state.Files.Changed(state.Board, path),
            };
        }
        Take(step, path);
    }

    void Take(FileStep step, string path)
    {
        switch (step)
        {
            case FileStep.Pin:
                Background.Run("pinning a screenshot", () => PinAsync(path));
                break;
            case FileStep.Redraw:
                Background.Run("redrawing a screenshot", () => RedrawAfterSettleAsync(path));
                break;
            case FileStep.CheckGone:
                Background.Run("checking a screenshot that went", () => CheckGoneAsync(path));
                break;
        }
    }

    /// <summary>Pins a new image file, decoded patiently as it may still be being written, and reveals the board.</summary>
    async Task PinAsync(string path)
    {
        Thumb thumb;
        try
        {
            thumb = await Thumbs.MakePatientlyAsync(path, FolderEvents.WritePatience).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DevTrace.Write($"screenshots: cannot read {Path.GetFileName(path)}: {e.Message}");
            return;
        }
        finally
        {
            lock (state.Gate)
            {
                state.Files.Finished(path);
            }
        }
        if (prints.Pin(path, thumb, Origin.Folder, Prints.Live()) is PinOutcome.New added)
        {
            await reveal.ForNewAsync().ConfigureAwait(false);
            prints.AnnounceLater(added.Aged);
        }
    }

    /// <summary>A pinned file was written to: redraws it once the writing settles.</summary>
    async Task RedrawAfterSettleAsync(string path)
    {
        await Task.Delay(FolderEvents.UpdateSettle).ConfigureAwait(false);
        lock (state.Gate)
        {
            state.Files.Finished(path);
        }
        await RedrawAsync(path).ConfigureAwait(false);
    }

    /// <summary>A pinned file went: unpins it, unless it is back after the grace (an editor saving).</summary>
    async Task CheckGoneAsync(string path)
    {
        await Task.Delay(FolderEvents.RemoveGrace).ConfigureAwait(false);
        if (Path.Exists(path))
        {
            await RedrawAsync(path).ConfigureAwait(false);
        }
        else
        {
            unpinning.RemovePath(path, Removal.Quiet);
        }
    }

    /// <summary>Shows a pinned file's new contents: a note's text or a fresh thumbnail, unless the file is unchanged.</summary>
    async Task RedrawAsync(string path)
    {
        var stamp = FileStamp.Of(path);
        Kind? what;
        lock (state.Gate)
        {
            what = FolderEvents.WhatToRedraw(state.Board.FindPath(path), stamp);
        }
        try
        {
            switch (what)
            {
                case Kind.Note:
                    prints.UpdateNote(path, NoteFiles.Read(path));
                    break;
                case Kind.Image:
                    prints.UpdateThumb(path, await Thumbs.MakePatientlyAsync(path, FolderEvents.WritePatience).ConfigureAwait(false));
                    break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DevTrace.Write($"screenshots: cannot redraw {Path.GetFileName(path)}: {e.Message}");
        }
    }

    public void Dispose() => _watch?.Dispose();
}
