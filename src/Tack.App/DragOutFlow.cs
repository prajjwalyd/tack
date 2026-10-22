using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Contract;
using Tack.Core.Captures;
using Tack.Core.Diagnostics;
using Tack.Core.Thumbnails;
using Tack.Windows.Imaging;
using Tack.Windows.Shell;

namespace Tack.App;

/// <summary>
/// Dragging a print's file out of the board to another app: the drag is
/// marked in the shared view (so the board never tucks under it), the shell's
/// drag loop runs on the UI thread, and afterwards the print is unpinned
/// quietly if the drop moved its file away.
/// </summary>
/// <param name="window">The board's window, which the drag starts from.</param>
sealed class DragOutFlow(AppState state, PendingSaves saves, Unpinning unpinning, BoardFeed feed, DispatcherQueue ui, nint window)
{
    /// <summary>When to look whether the dropped file is still there: Explorer may finish a move after the drag returns.</summary>
    static readonly TimeSpan[] Rechecks = [TimeSpan.Zero, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1500)];

    /// <summary>
    /// Starts dragging the print out and returns at once. The board hears
    /// <see cref="BoardFeed.PrintDragging"/> before the drag and
    /// <see cref="BoardFeed.PrintDragEnded"/> once the drop is over.
    /// </summary>
    public void Start(string id) => Background.Run("dragging a print out", () => StartAsync(id));

    async Task StartAsync(string id)
    {
        string? path;
        lock (state.Gate)
        {
            path = state.Board.Find(id)?.Path;
        }
        if (path is null)
        {
            return;
        }
        // A capture pinned moments ago may still be being written.
        await saves.WaitAsync(path, PendingSaves.Patience).ConfigureAwait(false);
        Thumb? thumb;
        lock (state.Gate)
        {
            if (state.Board.Find(id) is not { } print)
            {
                return;
            }
            (path, thumb) = (print.Path, print.Thumb);
            state.View.Dragging = id;
        }
        var image = thumb is null ? null : await DragImage.FromAsync(thumb.Jpeg).ConfigureAwait(false);
        feed.RaisePrintDragging(id);
        // Low, so the board has drawn its dragging look before the UI thread sits in the drag.
        if (!ui.TryEnqueue(DispatcherQueuePriority.Low, () => Drag(id, path, image)))
        {
            Finish(id, path);
        }
    }

    /// <summary>The shell's drag loop, on the UI thread: it returns once the button is released somewhere.</summary>
    void Drag(string id, string path, DragImage? image)
    {
        try
        {
            var effect = DragOut.DragFile(window, path, image);
            DevTrace.Write($"drag: drop effect {effect}");
        }
        catch (Exception e)
        {
            DevTrace.Write($"drag: failed: {e.Message}");
        }
        finally
        {
            Finish(id, path);
        }
    }

    void Finish(string id, string path)
    {
        lock (state.Gate)
        {
            if (state.View.Dragging == id)
            {
                state.View.Dragging = null;
            }
        }
        feed.RaisePrintDragEnded(id);
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
