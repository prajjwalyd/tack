using Tack.Core.Diagnostics;
using Tack.Core.Pinboard.Input;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// Hands what finished gestures ask for to the app: copy, open, edit, drag
/// out, the menu at the pointer. UI thread only.
/// </summary>
sealed class PointerOutcomes(BoardContext board)
{
    /// <summary>Does each in turn. A print gone or leaving is only still copied: that click was made.</summary>
    public void Run(IReadOnlyList<GestureOutcome> outcomes)
    {
        foreach (var (action, id) in outcomes)
        {
            if (action == GestureAction.Copy)
            {
                _ = Copy(id);
                continue;
            }
            if (board.State.Find(id) is not { Leaving: false })
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
                case GestureAction.StartDrag:
                    board.Actions.StartDrag(id);
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
