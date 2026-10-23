using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Input;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// The pointer on prints: click to copy, double click to open, hold to edit,
/// drag out, right click for the menu (<see cref="GestureMachine"/> decides
/// which), the Keep and unpin buttons (<see cref="PointerButtons"/>), and the
/// hover look, one print at a time with its lean (<see cref="PointerHover"/>).
/// A press captures the pointer to its print's slot; the window's root hears
/// every move and release, so a press ends wherever the button comes up.
/// Nothing waits while tucked: a click still waiting copies as the board goes. UI thread only.
/// </summary>
sealed class Gestures : BoardPart
{
    readonly GestureMachine _machine = new();
    readonly Dictionary<UIElement, BoardItem> _slots = [];
    readonly PointerHover _hover;
    readonly PointerOutcomes _outcomes;
    readonly Brush _hitArea = new SolidColorBrush(Colors.Transparent);
    DispatcherQueueTimer? _due;
    DispatcherQueueTimer? _rehover;
    BoardItem? _sinking;
    (UIElement Slot, Pointer Pointer)? _captured;

    public Gestures(BoardContext board) : base(board)
    {
        _hover = new PointerHover(board, _slots);
        _outcomes = new PointerOutcomes(board);
        var root = board.Layers.Root;
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(RootMoved), true);
        root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(RootReleased), true);
    }

    BoardState State => Board.State;

    /// <summary>Gives <paramref name="item"/> the hover look (the keyboard's selection), taking it from any other print.</summary>
    public void Hover(BoardItem item) => _hover.Set(item);

    /// <summary>Nothing is hovered any more.</summary>
    public void ClearHover() => _hover.Clear();

    /// <summary>The keyboard asked for a print's menu: a right click in the next moments opens none (the menu key raises both).</summary>
    public void KeyboardMenuAsked() => _machine.KeyboardMenuAsked(Now());

    /// <summary>The hovered print moved from under the pointer (the row reordered): it loses the hover, and whatever is under the pointer once the row has moved gains it.</summary>
    public void HoveredMoved()
    {
        _hover.Clear();
        Board.Timers.Cancel(_rehover);
        _rehover = Board.Timers.Later(Board.Clock.LayoutMs, () =>
        {
            _rehover = null;
            _hover.Rehover();
        });
    }

    /// <summary>The row started (<paramref name="moving"/>) or stopped scrolling under the pointer. The scroller calls this.</summary>
    public void ScrollChanged(bool moving)
    {
        _hover.Scrolling = moving;
        if (!moving)
        {
            _hover.Rehover();
        }
        else if (State.Pressing is null)
        {
            _hover.Clear();
        }
    }

    public override void Added(BoardItem item, bool animate, Flight? flight)
    {
        var slot = (Panel)item.View.Slot.Element;
        // The slot's whole box takes the pointer, a steady frame however the card moves.
        slot.Background = _hitArea;
        _slots[slot] = item;
        slot.PointerEntered += (_, e) =>
        {
            _hover.Set(item);
            _hover.Lean(item, e.GetCurrentPoint(slot).Position);
        };
        slot.PointerMoved += (_, e) => _hover.Lean(item, e.GetCurrentPoint(slot).Position);
        slot.PointerExited += (_, e) =>
        {
            // Leaving the paper or a button for the rest of the slot is still over the print.
            var at = e.GetCurrentPoint(slot).Position;
            var inside = at.X >= 0 && at.Y >= 0 && at.X < item.W && at.Y < item.H;
            if (!inside && _machine.PressId != item.Id)
            {
                _hover.Leave(item);
            }
        };
        slot.PointerPressed += (_, e) => Pressed(item, slot, e);
        slot.PointerCanceled += (_, _) => Cancelled(item);
        slot.RightTapped += (_, e) =>
        {
            e.Handled = true;
            Menu(item);
        };
        PointerButtons.Wire(item, Board);
    }

    public override void Removed(BoardItem item, Removal how)
    {
        _slots.Remove(item.View.Slot.Element);
        _machine.Forget(item.Id);
        if (_captured?.Slot == item.View.Slot.Element)
        {
            Release();
        }
        // The controller already let go of it in the state; the look goes too.
        if (item.View.Hovered)
        {
            item.View.SetHover(false);
        }
        Sync();
    }

    public override void DragStarted(string id, BoardItem? item)
    {
        if (item is not null)
        {
            _hover.Leave(item);
            item.View.SetDragging(true);
        }
        Sync();
    }

    public override void DragEnded(string id, BoardItem? item)
    {
        if (_machine.PressId == id)
        {
            _machine.EndPress();
        }
        item?.View.SetDragging(false);
        Sync();
    }

    /// <summary>The window went click-through: the pointer is gone, and so is the hover unless a press is held.</summary>
    public override void PointerLeft()
    {
        _hover.Pointer = null;
        if (_machine.PressId is null)
        {
            _hover.Clear();
        }
    }

    public override void Tucking()
    {
        Run(_machine.Flush(Now()));
        Release();
        _hover.Clear();
        _hover.Pointer = null;
        _hover.Scrolling = false;
        Board.Timers.Cancel(_rehover);
        _rehover = null;
        Sync();
    }

    void Pressed(BoardItem item, Panel slot, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Board.Layers.Root);
        var button = PointerKind.Of(point.Properties.PointerUpdateKind);
        if (button != PointerButton.Left || item.Leaving || !State.Revealed)
        {
            return;
        }
        e.Handled = true;
        Release();
        Run(_machine.Press(item.Id, button, point.Position.X, point.Position.Y, Now()));
        if (_machine.PressId == item.Id && _machine.Sinking)
        {
            if (slot.CapturePointer(e.Pointer))
            {
                _captured = (slot, e.Pointer);
            }
            _hover.Set(item);
        }
        Sync();
    }

    void RootMoved(object sender, PointerRoutedEventArgs e)
    {
        var at = e.GetCurrentPoint(Board.Layers.Root).Position;
        _hover.Pointer = at;
        if (_machine.Sinking)
        {
            Run(_machine.Move(at.X, at.Y, Now()));
            Sync();
        }
    }

    void RootReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_machine.PressId is not { } id)
        {
            return;
        }
        var point = e.GetCurrentPoint(Board.Layers.Root);
        Run(_machine.Release(PointerKind.Of(point.Properties.PointerUpdateKind), Now()));
        Release();
        Sync();
        if (State.Find(id) is { } item && _hover.At(point.Position) != item)
        {
            _hover.Leave(item);
        }
    }

    void Cancelled(BoardItem item)
    {
        if (_machine.PressId == item.Id)
        {
            _machine.Cancel();
            Sync();
        }
    }

    void Menu(BoardItem item)
    {
        if (item.Leaving || !State.Revealed)
        {
            return;
        }
        Run(_machine.Menu(item.Id, Now()));
        if (_machine.PressId is null)
        {
            Release();
        }
        Sync();
    }

    /// <summary>Does what the finished gestures ask.</summary>
    void Run(IReadOnlyList<GestureOutcome> outcomes)
    {
        if (_machine.Dragged)
        {
            // The shell's drag takes the pointer from here.
            Release();
        }
        _outcomes.Run(outcomes);
    }

    /// <summary>Brings the board's state and the sinking look up to the machine's, and waits for whatever falls due next.</summary>
    void Sync()
    {
        State.Pressing = _machine.PressId;
        var sinking = _machine.Sinking && _machine.PressId is { } id ? State.Find(id) : null;
        if (sinking != _sinking)
        {
            _sinking?.View.SetPressing(false);
            sinking?.View.SetPressing(true);
            _sinking = sinking;
        }
        Board.Timers.Cancel(_due);
        _due = null;
        if (_machine.NextDue is { } due && State.Revealed)
        {
            _due = Board.Timers.Later(MotionClock.Fixed(due - Now(), "a gesture's times are physical"), () =>
            {
                _due = null;
                Run(_machine.Due(Now()));
                Sync();
            });
        }
    }

    /// <summary>Lets go of the pointer a press captured, if any.</summary>
    void Release()
    {
        if (_captured is { } captured)
        {
            _captured = null;
            captured.Slot.ReleasePointerCapture(captured.Pointer);
        }
    }

    static long Now() => Environment.TickCount64;
}
