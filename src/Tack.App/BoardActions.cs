using Tack.App.Pinboard.Contract;
using Tack.Core.Geometry;
using Tack.Core.Reveal;

namespace Tack.App;

/// <summary>
/// The app's side of what the board asks for, handed on to the service that
/// does each. Every call is made on the UI thread and returns at once: the
/// clipboard, the disk and the shell work on other threads, and a capture
/// still being written is waited for off the UI thread.
/// </summary>
sealed class BoardActions(
    AppState state,
    Reveal reveal,
    Prints prints,
    Unpinning unpinning,
    PrintCommands commands,
    PrintMenu menu,
    DragOutFlow drag,
    Pinning pinning,
    ImageDrops drops,
    SnipTip tip) : IBoardActions
{
    /// <summary>
    /// The board hears every change through the feed from startup on, so this
    /// is the board as it stands when asked: prints the feed has brought
    /// already, or is still bringing, come again here and are only updated.
    /// </summary>
    public BoardSnapshot Ready()
    {
        lock (state.Gate)
        {
            return new BoardSnapshot([.. state.Board.Prints], BoardLook.Of(state.Settings));
        }
    }

    public void SetBoardRect(Rect? rect)
    {
        lock (state.Gate)
        {
            state.View.BoardRect = rect;
        }
    }

    public void SetTip(Rect? rect) => tip.Set(rect);

    public Task CopyAsync(string id) => commands.CopyAsync(id);

    public void Open(string id) => commands.Open(id);

    public void Edit(string id) => commands.Edit(id);

    /// <summary>Explorer on the print's folder with its file selected, as the menu's "Show in Explorer".</summary>
    public void ShowInFolder(string id) => commands.ShowInFolder(id);

    public Task<string?> DragOutAsync(string id) => drag.BeginAsync(id);

    public void DragOutEnded(string id, string? path) => drag.End(id, path);

    public void Discard(string id) => unpinning.Remove(id, Removal.Fall);

    public void SetKept(string id, bool kept) => prints.SetKept(id, kept);

    public void ShowMenu(string id, (int X, int Y)? at) => menu.Show(id, at);

    public void PinText(string text) => pinning.PinDroppedText(text);

    public void PinImage(string name, byte[] bytes) => drops.Pin(name, bytes);

    public bool TakeFocus()
    {
        lock (state.Gate)
        {
            if (!state.View.Shown || state.View.Reason != RevealReason.Hotkey)
            {
                return false;
            }
        }
        return reveal.Focus.Take();
    }

    public void ReleaseFocus() => reveal.Focus.GiveBack();

    public void Hide() => reveal.Tuck();
}
