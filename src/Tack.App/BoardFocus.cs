using Tack.Core.Diagnostics;
using Tack.Windows.Foreground;

namespace Tack.App;

/// <summary>
/// The board and the keyboard. Brought down with the show-or-hide shortcut,
/// the board takes the keyboard so the arrow keys, Enter, Delete and Esc work
/// on it, and when it goes the window that had the keyboard gets it back.
/// Brought down any other way, it never takes the keyboard. On the UI thread.
/// </summary>
sealed class BoardFocus(nint board)
{
    nint? _previous;
    bool _taken;

    /// <summary>Takes the keyboard; whether the board has it.</summary>
    public bool Take()
    {
        if (Focus.Take(board) is not { } previous)
        {
            DevTrace.Write("keyboard: the board could not take the focus");
            return false;
        }
        _previous = previous != board ? previous : null;
        _taken = true;
        return true;
    }

    /// <summary>Gives the keyboard back to the window that had it, if the board still has it.</summary>
    public void GiveBack()
    {
        if (!_taken)
        {
            return;
        }
        _taken = false;
        Focus.GiveBack(board, _previous);
        _previous = null;
    }
}
