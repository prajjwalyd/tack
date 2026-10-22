using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Contract;
using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Thumbnails;

namespace Tack.App;

/// <summary>
/// Pinning, keeping and redrawing prints, from the app's side: each change is
/// made on the board model under the app's lock, saved, told to the board, and
/// for a capture or a note leaving the board followed by moving its file to
/// the Recycle Bin. The board hears every change from startup on, raised
/// under the lock so its events keep the model's order. A save only hands a
/// snapshot to the saver, so none of this waits for the disk.
/// </summary>
sealed class Prints(AppState state, OwnedFiles files, BoardFeed feed, DispatcherQueue ui)
{
    /// <summary>A fresh screenshot, arriving now.</summary>
    public static Arrival Live() => new Arrival.Live(Now);

    /// <summary>Now on the monotonic clock, which arrivals and repeats are timed by.</summary>
    public static TimeSpan Now => Stopwatch.GetElapsedTime(0);

    /// <summary>
    /// Pins a screenshot. A live one has just arrived: it animates in (flying
    /// in, with a <paramref name="flight"/>), joins the front of the history and
    /// may be the other half of an auto-saved screenshot. A restored one comes
    /// back as it was saved. A new print's aged prints are the caller's to
    /// announce (<see cref="AnnounceLater"/>).
    /// </summary>
    public PinOutcome Pin(string path, Thumb thumb, Origin origin, Arrival arrival, Flight? flight = null) =>
        PinWith(arrival, flight, (board, now) => board.Pin(path, thumb, origin, arrival, now));

    /// <summary>Pins a note whose text is saved at <paramref name="path"/>, live or restored.</summary>
    public PinOutcome PinNote(string path, NoteBody body, Arrival arrival) =>
        PinWith(arrival, null, (board, now) => board.PinNote(path, body, arrival, now));

    /// <summary>
    /// Keeps a print, or stops keeping it. The board hears its new state, the
    /// prints that aged out because of it, then the new order if it moved.
    /// </summary>
    public void SetKept(string id, bool kept)
    {
        KeepChange? change;
        lock (state.Gate)
        {
            change = state.Board.SetKept(id, kept, History.NowMs());
            if (change is null)
            {
                return;
            }
            state.Save();
            feed.RaisePrintUpdated(change.Print);
            Tell(change.Aged);
            if (change.Moved)
            {
                feed.RaiseOrderChanged(state.Board.Order());
            }
        }
        files.DiscardOf(change.Aged);
    }

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
            Tell(aged);
        }
        files.DiscardOf(aged);
    }

    /// <summary>
    /// Tells the board of prints that aged out of the history for a new one,
    /// once the reveal for it (if any, asked for just before) has been told:
    /// they fall from a board that is down, or, still tucked, go quietly.
    /// </summary>
    public void AnnounceLater(IReadOnlyList<Print> aged)
    {
        if (aged.Count > 0)
        {
            // Queued behind the reveal's own step on the UI thread, which tells the board first.
            ui.TryEnqueue(() => Announce(aged));
        }
    }

    /// <summary>Tells the board now of prints that aged out of the history, as <see cref="RemovalNow"/> says.</summary>
    public void Announce(IReadOnlyList<Print> aged)
    {
        if (aged.Count > 0)
        {
            lock (state.Gate)
            {
                Tell(aged);
            }
        }
    }

    /// <summary>Points a print at its file's new place. Returns the old path, so a move that fails can be undone.</summary>
    public string? Repoint(string id, string path, Origin origin)
    {
        lock (state.Gate)
        {
            if (state.Board.Repoint(id, path, origin) is not { } moved)
            {
                return null;
            }
            state.Save();
            feed.RaisePrintUpdated(moved.Print);
            return moved.OldPath;
        }
    }

    /// <summary>Redraws a print whose file was edited.</summary>
    public void UpdateThumb(string path, Thumb thumb)
    {
        lock (state.Gate)
        {
            if (state.Board.UpdateThumb(path, thumb) is { } print)
            {
                feed.RaisePrintUpdated(print);
            }
        }
    }

    /// <summary>A note's file was written to: shows its new text.</summary>
    public void UpdateNote(string path, NoteBody body)
    {
        lock (state.Gate)
        {
            if (state.Board.UpdateNote(path, body) is { } print)
            {
                feed.RaisePrintUpdated(print);
            }
        }
    }

    /// <summary>How a print leaving on its own is shown: falling if anyone can see the board, a quiet fade otherwise. Under the lock.</summary>
    Removal RemovalNow() => state.View.Shown ? Removal.Fall : Removal.Quiet;

    /// <summary>Tells the board the prints are gone, as <see cref="RemovalNow"/> says. Under the lock.</summary>
    void Tell(IReadOnlyList<Print> aged)
    {
        var how = RemovalNow();
        foreach (var print in aged)
        {
            feed.RaisePrintRemoved(print.Id, how);
        }
    }

    /// <summary>
    /// Makes a pin on the board and carries out what came of it. A live pin is
    /// saved; a restored one is not, since the restore saves once when it is
    /// over (and every save meanwhile still lists the prints it has yet to put back).
    /// </summary>
    PinOutcome PinWith(Arrival arrival, Flight? flight, Func<Board, long, PinOutcome> pin)
    {
        var live = arrival is Arrival.Live;
        PinOutcome outcome;
        lock (state.Gate)
        {
            outcome = pin(state.Board, History.NowMs());
            switch (outcome)
            {
                case PinOutcome.Merged merged:
                    state.Save();
                    feed.RaisePrintUpdated(merged.Print);
                    break;
                case PinOutcome.New added:
                    if (live)
                    {
                        state.Save();
                    }
                    feed.RaisePrintAdded(added.Print, live, flight);
                    feed.RaiseOrderChanged(state.Board.Order());
                    break;
                case PinOutcome.Expired expired:
                    if (live && expired.Aged.Count > 0)
                    {
                        state.Save();
                    }
                    Tell(expired.Aged);
                    break;
            }
        }
        switch (outcome)
        {
            case PinOutcome.Merged merged:
                // The user's own file now stands in for Tack's copy.
                files.Discard([merged.Capture]);
                break;
            case PinOutcome.New added:
                files.DiscardOf(added.Aged);
                break;
            case PinOutcome.Expired expired:
                files.DiscardOf([expired.Print, .. expired.Aged]);
                break;
        }
        return outcome;
    }
}
