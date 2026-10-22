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
/// <param name="window">The board's window, which owns the menu.</param>
sealed class PrintMenu(AppState state, PrintCommands commands, Prints prints, Unpinning unpinning, DispatcherQueue ui, nint window)
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
            // The board's window is never activated: the menu leaves the keyboard where it was.
            if (at is { } point)
            {
                PopupMenu.ShowAt(window, (origin.X + point.X, origin.Y + point.Y), items, takeForeground: false);
            }
            else
            {
                PopupMenu.ShowAtPointer(window, items, takeForeground: false);
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
