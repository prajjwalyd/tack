using Tack.App.Pinboard.Contract;
using Tack.Core.Captures;
using Tack.Core.Files;
using Tack.Core.Prints;
using Tack.Core.Reveal;

namespace Tack.App;

/// <summary>
/// Taking prints off the board: one at a time (the x button, Delete, a
/// file that went), all the unkept ones in a ripple (the tray's Clear), or off the
/// board and into the Recycle Bin. Each is unpinned under the app's lock,
/// saved, and told to the board.
/// </summary>
sealed class Unpinning(AppState state, Storage storage, PendingSaves saves, OwnedFiles files, BoardFeed feed)
{
    /// <summary>The beat between pins popping out in Clear's ripple, at Slow. Class A.</summary>
    static readonly TimeSpan RippleGap = TimeSpan.FromMilliseconds(45);

    /// <summary>Unpins a print and, if it is a capture or a note, recycles its file.</summary>
    public Print? Remove(string id, Removal how)
    {
        if (Unpin(id, how) is not { } print)
        {
            return null;
        }
        files.DiscardOf([print]);
        return print;
    }

    /// <summary>Unpins a print and leaves its file alone.</summary>
    public Print? Unpin(string id, Removal how)
    {
        lock (state.Gate)
        {
            if (state.Board.Remove(id) is not { } print)
            {
                return null;
            }
            state.Save();
            feed.RaisePrintRemoved(print.Id, how);
            return print;
        }
    }

    /// <summary>The file is gone already (deleted, or moved by a drop into a folder): unpins its print and leaves the file alone.</summary>
    public void RemovePath(string path, Removal how)
    {
        string? id;
        lock (state.Gate)
        {
            id = state.Board.FindPath(path)?.Id;
        }
        if (id is not null)
        {
            Unpin(id, how);
        }
    }

    /// <summary>
    /// Unpins every print that is not kept, one after another so the pins pop
    /// out in a ripple. Kept prints were kept on purpose; only Unpin removes them.
    /// </summary>
    public void Clear()
    {
        List<string> ids;
        TimeSpan ripple;
        lock (state.Gate)
        {
            ids = [.. state.Board.Prints.Where(p => !p.Kept).Select(p => p.Id)];
            // The pins pop at the speed's pace, so the ripple's beat does too.
            ripple = state.Settings.Reveal.Scaled(RippleGap);
        }
        Background.Run("clearing the board", async () =>
        {
            for (var n = 0; n < ids.Count; n++)
            {
                if (n > 0)
                {
                    await Task.Delay(ripple).ConfigureAwait(false);
                }
                Remove(ids[n], Removal.Fall);
            }
        });
    }

    /// <summary>
    /// "Move to Recycle Bin": unpinned first, so the print falls rather than
    /// fading as it would for a file that vanished, then its file, whoever's
    /// it is, goes to the Recycle Bin (never deleted outright).
    /// </summary>
    public void Recycle(string id)
    {
        if (Unpin(id, Removal.Fall) is not { } print)
        {
            return;
        }
        saves.WhenSaved(print.Path, path =>
            Background.Run("recycling a print's file", () => Recycling.RecycleFile(path, storage.Bin)));
    }
}
