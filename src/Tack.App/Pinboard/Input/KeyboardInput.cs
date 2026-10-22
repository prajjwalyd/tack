using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Input;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// The keyboard on the board, only after the show-or-hide shortcut brought it
/// down: the board takes the keyboard and focuses the first print (or the one
/// focused last time), which gets the focus ring and, once the board is at
/// rest, the hover look. Left, Right, Home and End move along the row and
/// scroll the print into view; Enter copies, Ctrl+Enter opens, K keeps,
/// Delete unpins, the menu key or Shift+F10 opens the menu under the print,
/// Esc tucks, and Tab stays in the row (<see cref="KeyboardNav"/>). A pointer
/// press hides the ring until the next key. When the focused print is
/// unpinned the next one takes the focus, else the previous. On tuck the
/// board lets go and the keyboard goes back to the app that had it.
/// </summary>
sealed class KeyboardInput : BoardPart
{
    readonly KeyboardFocus _focus;
    DispatcherQueueTimer? _hoverLater;

    /// <summary>The board took the keyboard for this reveal and owes it back.</summary>
    bool _took;

    bool _wired;

    public KeyboardInput(BoardContext board) : base(board)
    {
        _focus = new KeyboardFocus(board);
        board.Layers.Board.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown), false);
    }

    /// <summary>The print with the keyboard's focus: the board's selection.</summary>
    public BoardItem? Focused => _focus.Item;

    BoardState State => Board.State;

    /// <summary>A screen reader selected <paramref name="item"/>: the focus moves there, ring or not as it was (the keyboard only if the board has it).</summary>
    public void Select(BoardItem item) => FocusOn(item);

    /// <summary>The focused print gets its hover look back, if the keyboard drives (the row stopped scrolling).</summary>
    public void Rehover()
    {
        if (_focus.Item is { } item && _focus.Ringed && State.Revealed)
        {
            Board.Parts.Gestures.Hover(item);
        }
    }

    /// <summary>Keeps <paramref name="item"/>, or stops keeping it.</summary>
    public void Keep(BoardItem item)
    {
        if (item.Leaving)
        {
            return;
        }
        // phase 3: P4 fills this in: the keep press plays as for the Keep button,
        // with Board.Parts.Row.HoldOrder over it.
        Board.Actions.SetKept(item.Id, !item.Print.Kept);
    }

    /// <summary>The first reveal, every part made: from now on a pointer press hides the ring.</summary>
    public override void Synced()
    {
        if (!_wired)
        {
            _wired = true;
            Board.Parts.Gestures.PressStarted += () => _focus.Ringed = false;
        }
    }

    public override void Revealed(RevealReason reason, bool wasRevealed)
    {
        if (reason != RevealReason.Hotkey)
        {
            return;
        }
        _took = true;
        _focus.Ringed = true;
        FocusOn(_focus.Item ?? State.Row.FirstOrDefault(), wasRevealed ? 0 : Board.Clock.RestMs(State.Reduced));
        if (Board.Actions.TakeFocus())
        {
            _focus.Focus();
        }
        // The window comes to the front a moment after it asks: focus again then, in case the first was lost.
        Board.Queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_took && State.Revealed)
            {
                _focus.Focus();
            }
        });
    }

    /// <summary>The ring goes and the board lets go of the keyboard (the pointer's side clears the hover look).</summary>
    public override void Tucking()
    {
        Board.Timers.Cancel(_hoverLater);
        _hoverLater = null;
        _focus.Ringed = false;
        _focus.Blur();
        if (_took)
        {
            _took = false;
            Board.Actions.ReleaseFocus();
        }
    }

    public override void Removed(BoardItem item, Removal how)
    {
        if (!_focus.Holds(item))
        {
            return;
        }
        var hadKeyboard = _focus.HasKeyboard(item);
        _focus.Rove(null);
        if (!hadKeyboard && !_took)
        {
            return;
        }
        var row = State.Row.ToList();
        var next = KeyboardNav.AfterRemoval(item.IndexWhenRemoved, row.Count);
        FocusOn(next >= 0 ? row[next] : null);
    }

    void KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!State.Revealed || e.Handled)
        {
            return;
        }
        var row = State.Row.ToList();
        var focused = _focus.Item is { } item ? row.IndexOf(item) : -1;
        var key = KeyboardNav.KeyOf((int)e.Key);
        var command = KeyboardNav.Decide(key, KeyboardModifiers.Held(), e.KeyStatus.WasKeyDown, focused, row.Count);
        if (!command.Handled)
        {
            return;
        }
        e.Handled = true;
        if (command.ShowsRing)
        {
            _focus.Ringed = true;
        }
        Act(command, focused >= 0 ? row[focused] : null, row);
    }

    void Act(KeyboardCommand command, BoardItem? item, List<BoardItem> row)
    {
        switch (command.Action)
        {
            case KeyboardAction.Move:
                FocusOn(row[command.Target]);
                break;
            case KeyboardAction.Copy when item is not null:
                Background.Run("copying a print", () => Board.Actions.CopyAsync(item.Id));
                break;
            case KeyboardAction.Open when item is not null:
                Board.Actions.Open(item.Id);
                break;
            case KeyboardAction.Keep when item is not null:
                Keep(item);
                break;
            case KeyboardAction.Unpin when item is not null:
                Board.Actions.Discard(item.Id);
                break;
            case KeyboardAction.Menu when item is not null:
                // The right click the menu key also raises is not a second menu.
                Board.Parts.Gestures.KeyboardMenuAsked();
                Board.Actions.ShowMenu(item.Id, Board.Parts.Row.MenuPoint(item));
                break;
            case KeyboardAction.Tuck:
                Board.Actions.Hide();
                break;
        }
    }

    /// <summary>
    /// Focuses <paramref name="item"/>, or the board itself when there is none,
    /// shows it if the row scrolls, and gives it the hover look after
    /// <paramref name="hoverAfterMs"/> (not while the board still swings down).
    /// </summary>
    void FocusOn(BoardItem? item, double hoverAfterMs = 0)
    {
        _focus.Rove(item);
        // Only a board holding the keyboard moves it: a screen reader's pick never takes it from another app.
        if (_took)
        {
            _focus.Focus();
        }
        Board.Timers.Cancel(_hoverLater);
        _hoverLater = null;
        if (item is null)
        {
            Board.Parts.Gestures.ClearHover();
            return;
        }
        Board.Parts.Row.ShowPrint(item);
        if (hoverAfterMs > 0)
        {
            _hoverLater = Board.Timers.Later(hoverAfterMs, () =>
            {
                _hoverLater = null;
                Rehover();
            });
            return;
        }
        Rehover();
    }
}
