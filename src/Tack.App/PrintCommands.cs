using Tack.App.Pinboard.Contract;
using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Files;
using Tack.Core.Prints;
using Tack.Windows.Clipboard;
using Tack.Windows.Screenshots;
using Tack.Windows.Shell;

namespace Tack.App;

/// <summary>
/// What a print can be asked to do with its file: copy, open, edit, show in
/// Explorer, save to Pictures. None of it waits on the calling thread: the
/// clipboard and the shell work on threads of their own, and a capture still
/// being written is waited for first.
/// </summary>
sealed class PrintCommands(AppState state, PendingSaves saves, Prints prints, BoardFeed feed)
{
    /// <summary>The picture, or a note's text (a link's URL), onto the clipboard; then the board shows "Copied".</summary>
    public async Task CopyAsync(string id)
    {
        if (Find(id) is not { } print)
        {
            return;
        }
        try
        {
            if (print.Note is { } note)
            {
                await ClipboardWriter.CopyTextAsync(note.Link ?? note.Text).ConfigureAwait(false);
            }
            else
            {
                await saves.WaitAsync(print.Path, PendingSaves.Patience).ConfigureAwait(false);
                await ClipboardWriter.CopyImageAsync(print.Path).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DevTrace.Write($"copy: the print could not be copied: {e.Message}");
            return;
        }
        feed.RaisePrintCopied(id);
    }

    /// <summary>Opens the print: a picture or a note's text file in its default app, a link in the browser.</summary>
    public void Open(string id)
    {
        if (Find(id) is not { } print)
        {
            return;
        }
        if (print.Note?.Link is { } link)
        {
            ShellLauncher.OpenUrl(link);
            return;
        }
        saves.WhenSaved(print.Path, ShellLauncher.Open);
    }

    /// <summary>Edits the print's file: Paint for a picture. An edited note is read again when the board next comes down.</summary>
    public void Edit(string id)
    {
        if (Find(id) is { } print)
        {
            saves.WhenSaved(print.Path, ShellLauncher.Edit);
        }
    }

    /// <summary>Explorer on the print's folder with its file selected.</summary>
    public void ShowInFolder(string id)
    {
        if (Find(id) is { } print)
        {
            saves.WhenSaved(print.Path, Explorer.ShowInFolder);
        }
    }

    /// <summary>
    /// "Save to Pictures": a capture moves to the Screenshots folder and becomes
    /// an ordinary file there, no longer Tack's to clean up. It takes a free name
    /// there and never replaces a file, even one that turns up while it moves.
    /// </summary>
    public void SaveToPictures(string id)
    {
        if (Find(id) is not { Origin: Origin.Capture } print)
        {
            return;
        }
        var path = print.Path;
        Background.Run("saving a capture to Pictures", async () =>
        {
            await saves.WaitAsync(path, PendingSaves.Patience).ConfigureAwait(false);
            var folder = ScreenshotsFolder.Find();
            try
            {
                // Repointed before each try, so the folder watch sees a pinned file arrive rather than a new screenshot.
                FileMove.ToFreeName(path, folder, to => prints.Repoint(id, to, Origin.Folder) is not null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                DevTrace.Write($"cannot save {Path.GetFileName(path)} to {folder}: {e.Message}");
                prints.Repoint(id, path, Origin.Capture);
            }
        });
    }

    Print? Find(string id)
    {
        lock (state.Gate)
        {
            return state.Board.Find(id);
        }
    }
}
