using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Contract;
using Tack.Core.Pinboard.Input;
using Tack.Windows.Shell;

namespace Tack.App;

/// <summary>
/// A print's native right-click menu (<see cref="PrintMenuModel"/>), at the
/// pointer or, when the keyboard asked for it, under the print. While it is
/// open the board never tucks.
/// </summary>
/// <param name="owner">
/// Tack's hidden host window, which owns the menu. Not the board's window:
/// that one is never activated, and a menu whose owner cannot come to the
/// front does not close on a click elsewhere.
/// </param>
sealed class PrintMenu(AppState state, PrintCommands commands, Prints prints, Unpinning unpinning, DispatcherQueue ui, nint owner)
{
    /// <summary>
    /// Opens the menu for <paramref name="id"/> at <paramref name="at"/> (physical
    /// px relative to the window) or at the pointer. On the UI thread; the menu
    /// opens once the board's own handler has returned, since it is modal.
    /// </summary>
    public void Show(string id, (int X, int Y)? at) => ui.TryEnqueue(() => Open(id, at));

    void Open(string id, (int X, int Y)? at)
    {
        IReadOnlyList<PrintMenuEntry> entries;
        (int X, int Y) origin;
        lock (state.Gate)
        {
            if (state.Board.Find(id) is not { } print)
            {
                return;
            }
            entries = PrintMenuModel.For(print);
            origin = state.View.WindowPos;
            state.View.MenuOpen = true;
        }
        try
        {
            var items = entries.Select(e => e.Action is { } action ? new MenuItem(e.Label, () => Run(action, id)) : MenuItem.Separator).ToList();
            // The menu gives the keyboard back to whoever had it when it closes.
            if (at is { } point)
            {
                PopupMenu.ShowAt(owner, (origin.X + point.X, origin.Y + point.Y), items);
            }
            else
            {
                PopupMenu.ShowAtPointer(owner, items);
            }
        }
        finally
        {
            lock (state.Gate)
            {
                state.View.MenuOpen = false;
            }
        }
    }

    void Run(PrintMenuAction action, string id)
    {
        switch (action)
        {
            case PrintMenuAction.Copy:
                Background.Run("copying a print", () => commands.CopyAsync(id));
                break;
            case PrintMenuAction.Open:
                commands.Open(id);
                break;
            case PrintMenuAction.Edit:
                commands.Edit(id);
                break;
            case PrintMenuAction.ShowInExplorer:
                commands.ShowInFolder(id);
                break;
            case PrintMenuAction.SaveToPictures:
                commands.SaveToPictures(id);
                break;
            case PrintMenuAction.Keep:
                prints.SetKept(id, kept: true);
                break;
            case PrintMenuAction.StopKeeping:
                prints.SetKept(id, kept: false);
                break;
            case PrintMenuAction.Unpin:
                unpinning.Remove(id, Removal.Fall);
                break;
            case PrintMenuAction.Recycle:
                unpinning.Recycle(id);
                break;
        }
    }
}
