using Microsoft.UI.Input;
using Tack.Core.Diagnostics;
using Tack.Core.Pinboard.Input;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// Hands what finished gestures ask for to the app: copy, open, edit, drag
/// out (<see cref="PrintDrag"/>), the menu at the pointer. UI thread only.
/// </summary>
sealed class PointerOutcomes(BoardContext board)
{
    readonly PrintDrag _drag = new(board);

    /// <summary>
    /// Does each in turn. A print gone or leaving is only still copied: that
    /// click was made. <paramref name="point"/> is the pointer that moved, which
    /// a drag starts from.
    /// </summary>
    public void Run(IReadOnlyList<GestureOutcome> outcomes, PointerPoint? point)
    {
        foreach (var (action, id) in outcomes)
        {
            if (action == GestureAction.Copy)
            {
                _ = Copy(id);
                continue;
            }
            if (board.State.Find(id) is not { Leaving: false } item)
            {
                continue;
            }
            switch (action)
            {
                case GestureAction.Open:
                    board.Actions.Open(id);
                    break;
                case GestureAction.Edit:
                    board.Actions.Edit(id);
                    break;
                case GestureAction.StartDrag when point is not null:
                    _drag.Start(item, point);
                    break;
                case GestureAction.StartDrag:
                    // No pointer to drag with: the press ends as a drag that never started.
                    board.Actions.DragOutEnded(id, null);
                    break;
                case GestureAction.Menu:
                    board.Actions.ShowMenu(id, null);
                    break;
            }
        }
    }

    async Task Copy(string id)
    {
        try
        {
            await board.Actions.CopyAsync(id);
        }
        catch (Exception e)
        {
            DevTrace.Write($"board: copying {id} failed: {e.Message}");
        }
    }
}
