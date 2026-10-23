using Tack.Core.Geometry;

namespace Tack.App.Pinboard.Contract;

/// <summary>
/// What the board asks of the app. Called on the UI thread; each returns at
/// once, and anything slow (the clipboard, the disk, the shell) carries on on
/// the thread pool. Ids are <see cref="Core.Prints.Print.Id"/>; an id no longer
/// on the board does nothing.
/// </summary>
interface IBoardActions
{
    /// <summary>The prints already pinned, in row order, and how the board looks. From then on every change comes through <see cref="BoardFeed"/>.</summary>
    BoardSnapshot Ready();

    /// <summary>Where the board is, physical px relative to the window: where the window takes the pointer. Null: nowhere.</summary>
    void SetBoardRect(Rect? rect);

    /// <summary>The one-time tip shows at <paramref name="rect"/> (physical px relative to the window), or, null, it closed.</summary>
    void SetTip(Rect? rect);

    /// <summary>The picture, or a note's text (a link's URL), onto the clipboard; then <see cref="BoardFeed.PrintCopied"/>.</summary>
    Task CopyAsync(string id);

    /// <summary>The print in its default app, or a link in the browser, once a capture is written.</summary>
    void Open(string id);

    /// <summary>The shell's "edit": Paint for a picture, Notepad for a note.</summary>
    void Edit(string id);

    /// <summary>
    /// The board starts dragging the print's file out to another app: once a
    /// capture still being written is, the drag is marked (the board never
    /// tucks under it) and <see cref="BoardFeed.PrintDragging"/> comes. The
    /// file's path, or null if the print is gone.
    /// </summary>
    Task<string?> DragOutAsync(string id);

    /// <summary>
    /// The drag is over, or never started (<paramref name="path"/> null):
    /// <see cref="BoardFeed.PrintDragEnded"/> comes, and the print is unpinned
    /// quietly if the drop moved its file away.
    /// </summary>
    void DragOutEnded(string id, string? path);

    /// <summary>Unpins the print with a fall; a capture's or note's own file goes to the Recycle Bin.</summary>
    void Discard(string id);

    /// <summary>Keeps the print at the front of the row, or lets it rejoin the history.</summary>
    void SetKept(string id, bool kept);

    /// <summary>The print's menu, at <paramref name="at"/> (physical px relative to the window, for the keyboard) or at the pointer.</summary>
    void ShowMenu(string id, (int X, int Y)? at);

    /// <summary>Text dropped on the board: pinned as a note, or a link if it is one.</summary>
    void PinText(string text);

    /// <summary>A PNG or JPEG dropped on the board, at most 40 MB: saved among the captures and pinned like one.</summary>
    void PinImage(string name, byte[] bytes);

    /// <summary>The window takes the keyboard, only when the board came down for the show-or-hide shortcut. Whether it has it.</summary>
    bool TakeFocus();

    /// <summary>The keyboard goes back to the window that had it.</summary>
    void ReleaseFocus();

    /// <summary>Tucks the board away (Esc).</summary>
    void Hide();
}
