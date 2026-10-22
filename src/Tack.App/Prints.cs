using System.Diagnostics;
using Tack.Core.Captures;
using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Thumbnails;

namespace Tack.App;

/// <summary>
/// What can happen to a print, from the app's side: each change is made on the
/// board model under the app's lock, saved, and, for a capture or a note
/// leaving the board, followed by moving its file to the Recycle Bin. A save
/// only hands a snapshot to the saver, so none of this waits for the disk.
/// </summary>
sealed class Prints(AppState state, Storage storage, PendingSaves saves)
{
    /// <summary>A fresh screenshot, arriving now.</summary>
    public static Arrival Live() => new Arrival.Live(Now);

    /// <summary>Now on the monotonic clock, which arrivals and repeats are timed by.</summary>
    public static TimeSpan Now => Stopwatch.GetElapsedTime(0);

    /// <summary>
    /// Pins a screenshot. A live one has just arrived: it joins the front of the
    /// history and may be the other half of an auto-saved screenshot. A
    /// restored one comes back as it was saved.
    /// </summary>
    public PinOutcome Pin(string path, Thumb thumb, Origin origin, Arrival arrival) =>
        PinWith(arrival, (board, now) => board.Pin(path, thumb, origin, arrival, now));

    /// <summary>Pins a note whose text is saved at <paramref name="path"/>, live or restored.</summary>
    public PinOutcome PinNote(string path, NoteBody body, Arrival arrival) =>
        PinWith(arrival, (board, now) => board.PinNote(path, body, arrival, now));

    /// <summary>Lets go of the unkept prints that have grown too old.</summary>
    public void AgeOut()
    {
        IReadOnlyList<Print> aged;
        lock (state.Gate)
        {
            aged = state.Board.AgeOut(History.NowMs());
            if (aged.Count == 0)
            {
                return;
            }
            state.Save();
        }
        DiscardFiles(aged);
    }

    /// <summary>The file is gone already: unpins its print, if any, and leaves the file alone.</summary>
    public void RemovePath(string path)
    {
        lock (state.Gate)
        {
            if (state.Board.FindPath(path) is { } print && state.Board.Remove(print.Id) != null)
            {
                state.Save();
            }
        }
    }

    /// <summary>
    /// Sends capture and note files Tack no longer needs to the Recycle Bin,
    /// never deleting them outright, on the thread pool since the shell can take
    /// a moment. A capture still being written goes once it is written.
    /// </summary>
    public void DiscardCaptures(IEnumerable<string> paths)
    {
        var now = new List<string>();
        foreach (var path in paths)
        {
            if (saves.IsSaving(path))
            {
                saves.WhenSaved(path, written => DiscardCaptures([written]));
            }
            else
            {
                now.Add(path);
            }
        }
        if (now.Count == 0)
        {
            return;
        }
        Background.Run("recycling Tack's own files", () =>
        {
            foreach (var path in now)
            {
                Discard.Owned(path, storage.OwnFolders, storage.Bin);
            }
        });
    }

    /// <summary>
    /// Makes a pin on the board and carries out what came of it. A live pin is
    /// saved; a restored one is not, since the restore saves once when it is
    /// over (and every save meanwhile still lists the prints it has yet to put back).
    /// </summary>
    PinOutcome PinWith(Arrival arrival, Func<Board, long, PinOutcome> pin)
    {
        var live = arrival is Arrival.Live;
        PinOutcome outcome;
        lock (state.Gate)
        {
            outcome = pin(state.Board, History.NowMs());
            var save = outcome switch
            {
                PinOutcome.Merged => true,
                PinOutcome.New => live,
                PinOutcome.Expired expired => live && expired.Aged.Count > 0,
                _ => false,
            };
            if (save)
            {
                state.Save();
            }
        }
        switch (outcome)
        {
            case PinOutcome.Merged merged:
                // The user's own file now stands in for Tack's copy.
                DiscardCaptures([merged.Capture]);
                break;
            case PinOutcome.New added:
                DiscardFiles(added.Aged);
                break;
            case PinOutcome.Expired expired:
                DiscardFiles([expired.Print, .. expired.Aged]);
                break;
        }
        return outcome;
    }

    /// <summary>Recycles the files of captures and notes that left the board; screenshot files stay.</summary>
    void DiscardFiles(IEnumerable<Print> prints) => DiscardCaptures(prints.Where(p => p.Owned).Select(p => p.Path));
}
