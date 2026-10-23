using Tack.App.Pinboard.Contract;
using Tack.Core.Captures;

namespace Tack.App;

/// <summary>
/// The app's side of dragging a print's file out of the board to another app
/// (the board runs the drag itself, with WinUI's drag source): the drag is
/// marked in the shared view, so the board never tucks under it, and once it
/// is over the print is unpinned quietly if the drop moved its file away.
/// </summary>
sealed class DragOutFlow(AppState state, PendingSaves saves, Unpinning unpinning, BoardFeed feed)
{
    /// <summary>When to look whether the dropped file is still there: Explorer may finish a move after the drag returns.</summary>
    static readonly TimeSpan[] Rechecks = [TimeSpan.Zero, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1500)];

    /// <summary>
    /// Waits for a capture pinned moments ago that may still be being written,
    /// then marks the drag and raises <see cref="BoardFeed.PrintDragging"/>.
    /// The print's file, or null if the print is gone.
    /// </summary>
    public async Task<string?> BeginAsync(string id)
    {
        string? path;
        lock (state.Gate)
        {
            path = state.Board.Find(id)?.Path;
        }
        if (path is null)
        {
            return null;
        }
        await saves.WaitAsync(path, PendingSaves.Patience).ConfigureAwait(false);
        lock (state.Gate)
        {
            if (state.Board.Find(id) is not { } print)
            {
                return null;
            }
            path = print.Path;
            state.View.Dragging = id;
        }
        feed.RaisePrintDragging(id);
        return path;
    }

    /// <summary>
    /// The drag is over, or never started (<paramref name="path"/> null): the
    /// mark goes, <see cref="BoardFeed.PrintDragEnded"/> is raised, and the file
    /// is looked for a few times.
    /// </summary>
    public void End(string id, string? path)
    {
        lock (state.Gate)
        {
            if (state.View.Dragging == id)
            {
                state.View.Dragging = null;
            }
        }
        feed.RaisePrintDragEnded(id);
        if (path is null)
        {
            return;
        }
        Background.Run("checking a dragged file", async () =>
        {
            var waited = TimeSpan.Zero;
            foreach (var at in Rechecks)
            {
                await Task.Delay(at - waited).ConfigureAwait(false);
                waited = at;
                if (!File.Exists(path))
                {
                    unpinning.RemovePath(path, Removal.Quiet);
                    return;
                }
            }
        });
    }
}
