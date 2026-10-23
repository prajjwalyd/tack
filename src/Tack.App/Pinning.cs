using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Files;
using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Thumbnails;
using Tack.Windows.Clipboard;
using Tack.Windows.Imaging;
using Tack.Windows.Selection;

namespace Tack.App;

/// <summary>
/// Pinning the selection on purpose (Win+Alt+C): text becomes a note, a
/// picture a capture, image files prints of those files. Anything that cannot
/// be pinned gets a short notice instead. Text only ever reaches the board this
/// way, because the user asked: the selection is read only right after the pin
/// shortcut, as text through UI Automation where the app offers it, and
/// otherwise as the app's own copy, with what the user had on the clipboard
/// before put back.
/// </summary>
sealed class Pinning(AppState state, Prints prints, CaptureFlow captures, Reveal reveal, Notices notices, string notesFolder)
{
    /// <summary>
    /// Set while a selection is pinned: one at a time, and a press meanwhile is
    /// dropped (two grabs would snapshot and restore each other's copies).
    /// </summary>
    int _busy;

    /// <summary>The pin shortcut was pressed. On the UI thread, so it acts before the user can switch windows.</summary>
    public void OnShortcut()
    {
        // Now, while Win and Alt are still down: the keys go to this window only.
        var target = Target.InFront();
        SelectionGrab.MaskMenu();
        Background.Run("pinning the selection", () => PinSelectionAsync(target));
    }

    /// <summary>
    /// Text dropped on the board: pinned as a note (a link if it is one), on the
    /// thread pool since it writes a file. The pinning stays outside the trace
    /// call, which a release build leaves out with its argument.
    /// </summary>
    public void PinDroppedText(string text) =>
        Background.Run("pinning dropped text", async () =>
        {
            var outcome = await PinTextAsync(text).ConfigureAwait(false);
            DevTrace.Write($"dropped text: {outcome}");
        });

    /// <summary>
    /// Text sent from the phone board: pinned as a note (a link if it is one),
    /// with the same notices and reveal as the pin shortcut. Finishes once it is
    /// pinned or turned away.
    /// </summary>
    public async Task PinPhoneTextAsync(string text)
    {
        var outcome = await PinTextAsync(text).ConfigureAwait(false);
        DevTrace.Write($"phone text: {outcome}");
    }

    /// <summary>Copies the selection in <paramref name="target"/> and pins what it was. Takes up to about two seconds.</summary>
    async Task PinSelectionAsync(Target? target)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            DevTrace.Write("pin selection: ignored (still pinning the last selection)");
            return;
        }
        try
        {
            var grab = await SelectionGrab.GrabAsync(target).ConfigureAwait(false);
            var outcome = grab.KeysHeld ? "keys held too long, nothing sent" : await PinAsync(grab.What).ConfigureAwait(false);
            if (grab.Clipboard is ClipboardFate.Failed failed)
            {
                DevTrace.Write($"the clipboard could not be put back as it was: {failed.Why}");
                // Last, so it is the notice that stays: the next paste will not be what the user expects.
                notices.Show(Notices.NotRestored);
            }
            DevTrace.Write($"pin selection: {outcome}; clipboard {Describe(grab.Clipboard)}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    async Task<string> PinAsync(ClipboardContent what)
    {
        switch (what)
        {
            case ClipboardContent.TextContent text:
                return await PinTextAsync(text.Text).ConfigureAwait(false);
            case ClipboardContent.PictureContent picture:
                return $"picture: {await captures.PinPictureAsync(picture.Picture).ConfigureAwait(false)}";
            case ClipboardContent.FilesContent files:
                return await PinFilesAsync(files.Paths).ConfigureAwait(false);
            case ClipboardContent.PrivateContent:
                notices.Show(Notices.Private);
                return "private, not pinned";
            default:
                notices.Show(Notices.NothingSelected);
                return "nothing selected";
        }
    }

    /// <summary>Pins <paramref name="text"/> as a note (a link if it is one), live. Returns what happened, for the trace.</summary>
    async Task<string> PinTextAsync(string text)
    {
        if (NoteText.Body(text) is not { } body)
        {
            notices.Show(Notices.NothingSelected);
            return "blank text";
        }
        bool pinned;
        lock (state.Gate)
        {
            pinned = state.Board.FindNote(body.Text) != null;
        }
        if (pinned)
        {
            notices.Show(Notices.AlreadyPinned);
            return "already pinned";
        }
        string path;
        try
        {
            path = NoteFiles.Save(body, notesFolder, Timestamp.Of(DateTime.Now));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"cannot save a note: {e.Message}");
            notices.Show(Notices.Failed);
            return $"cannot save ({e.Message})";
        }
        if (prints.PinNote(path, body, Prints.Live()) is PinOutcome.New added)
        {
            await reveal.ForNewAsync().ConfigureAwait(false);
            prints.AnnounceLater(added.Aged);
            return $"{(body.Link is null ? "note" : "link")} of {body.Text.EnumerateRunes().Count()} characters pinned";
        }
        // Never shown: the same text was pinned a moment ago.
        DeleteUnshown(path);
        notices.Show(Notices.AlreadyPinned);
        return "already pinned";
    }

    /// <summary>Pins the image files among <paramref name="paths"/> where they are, like screenshot files.</summary>
    async Task<string> PinFilesAsync(IReadOnlyList<string> paths)
    {
        var images = PinnableFiles.Among(paths);
        if (images.Count == 0)
        {
            notices.Show(Notices.NotPinnable);
            return $"{paths.Count} file(s), none of them PNG or JPEG";
        }
        var (pinned, already) = (0, 0);
        var aged = new List<Print>();
        foreach (var path in images)
        {
            Thumb thumb;
            try
            {
                thumb = await Thumbs.MakeAsync(path).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }
            switch (prints.Pin(path, thumb, Origin.Folder, Prints.Live()))
            {
                case PinOutcome.New added:
                    pinned++;
                    aged.AddRange(added.Aged);
                    break;
                case PinOutcome.Exists:
                    already++;
                    break;
            }
        }
        if (pinned > 0)
        {
            await reveal.ForNewAsync().ConfigureAwait(false);
            prints.AnnounceLater(aged);
        }
        else
        {
            notices.Show(already > 0 ? Notices.AlreadyPinned : Notices.BadImage);
        }
        return $"{pinned} of {paths.Count} file(s) pinned";
    }

    /// <summary>Removes a note's file Tack wrote a moment ago and never showed.</summary>
    static void DeleteUnshown(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"cannot remove an unused note: {e.Message}");
        }
    }

    static string Describe(ClipboardFate fate) => fate switch
    {
        ClipboardFate.UntouchedFate => "untouched",
        ClipboardFate.RestoredFate => "restored",
        ClipboardFate.LeftNewerFate => "left as another app set it",
        _ => "NOT restored",
    };
}
