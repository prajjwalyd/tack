namespace Tack.Core.Pinboard.Input;

/// <summary>
/// What the pointer's presses on prints mean, decided from the presses alone
/// and the time each came (ms on any steady clock), so it runs without a
/// screen. A click copies once <see cref="DoubleClickMs"/> has passed with no
/// second press; a second press on the same print within it opens the print
/// instead; a press on another print copies the waiting click at once. A
/// press held <see cref="HoldMs"/> edits; one that moves more than
/// <see cref="DragPx"/> drags the file out. A right click opens the menu,
/// unless the keyboard asked for one in the last <see cref="KeyboardMenuMs"/>
/// (the menu key raises both). Only the left button presses.
/// </summary>
/// <remarks>
/// The caller asks again at <see cref="NextDue"/> through <see cref="Due"/>;
/// every other call first settles whatever fell due before it, so a timer
/// heard late changes nothing. These times are physical: the Speed setting
/// never changes them.
/// </remarks>
public sealed class GestureMachine
{
    /// <summary>How long a click waits for a second press before it copies.</summary>
    public const int DoubleClickMs = 260;

    /// <summary>How long a press is held to edit; the print sinks over the same time.</summary>
    public const int HoldMs = 520;

    /// <summary>How far a press moves, logical px in a straight line, before it is a drag.</summary>
    public const double DragPx = 4;

    /// <summary>After the keyboard asked for a menu, how long a right click opens none.</summary>
    public const int KeyboardMenuMs = 600;

    string? _pressId;
    double _x;
    double _y;
    long _pressedAt;
    bool _held;
    bool _dragged;
    bool _spent;
    string? _clickId;
    long _clickedAt;
    long? _keyboardMenuAt;

    /// <summary>The print a press is held on, until the button comes up (or, for a drag, until the drag ends).</summary>
    public string? PressId => _pressId;

    /// <summary>The press shows its sinking look: not yet held long enough, not a drag, not a double click's second press.</summary>
    public bool Sinking => _pressId is not null && !_held && !_dragged && !_spent;

    /// <summary>The press became a drag; it ends through <see cref="EndPress"/> once the drag is over.</summary>
    public bool Dragged => _pressId is not null && _dragged;

    /// <summary>The print a click waits to copy, if a second press does not come.</summary>
    public string? PendingCopy => _clickId;

    /// <summary>When something next falls due (a click to copy, a hold to edit): when to call <see cref="Due"/>. Null: nothing waits.</summary>
    public long? NextDue
    {
        get
        {
            long? due = _clickId is null ? null : _clickedAt + DoubleClickMs;
            if (Sinking)
            {
                var hold = _pressedAt + HoldMs;
                due = due is { } click ? Math.Min(click, hold) : hold;
            }
            return due;
        }
    }

    /// <summary>A button went down on print <paramref name="id"/> at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public IReadOnlyList<GestureOutcome> Press(string id, PointerButton button, double x, double y, long now)
    {
        var outcomes = Settle(now);
        if (button != PointerButton.Left)
        {
            return outcomes;
        }
        if (_clickId == id)
        {
            // Second press in time: open instead of copying twice. Its release is no click.
            _clickId = null;
            Begin(id, x, y, now);
            _spent = true;
            outcomes.Add(new(GestureAction.Open, id));
            return outcomes;
        }
        if (_clickId is { } other)
        {
            _clickId = null;
            outcomes.Add(new(GestureAction.Copy, other));
        }
        Begin(id, x, y, now);
        return outcomes;
    }

    /// <summary>The pointer moved to (<paramref name="x"/>, <paramref name="y"/>), in the units of the press.</summary>
    public IReadOnlyList<GestureOutcome> Move(double x, double y, long now)
    {
        var outcomes = Settle(now);
        if (Sinking && _pressId is { } id && Math.Sqrt((x - _x) * (x - _x) + (y - _y) * (y - _y)) > DragPx)
        {
            _dragged = true;
            outcomes.Add(new(GestureAction.StartDrag, id));
        }
        return outcomes;
    }

    /// <summary>A button came up: the press ends, and is a click if nothing else came of it.</summary>
    public IReadOnlyList<GestureOutcome> Release(PointerButton button, long now)
    {
        var outcomes = Settle(now);
        if (_pressId is not { } id)
        {
            return outcomes;
        }
        var click = Sinking && button == PointerButton.Left;
        EndPress();
        if (click)
        {
            (_clickId, _clickedAt) = (id, now);
        }
        return outcomes;
    }

    /// <summary>The press was taken away (the pointer was cancelled or lost): no click. A drag's press waits for the drag to end.</summary>
    public void Cancel()
    {
        if (!_dragged)
        {
            EndPress();
        }
    }

    /// <summary>A right click on <paramref name="id"/>: its menu, unless the keyboard just asked for one.</summary>
    public IReadOnlyList<GestureOutcome> Menu(string id, long now)
    {
        var outcomes = Settle(now);
        if (_keyboardMenuAt is { } asked && now - asked < KeyboardMenuMs)
        {
            return outcomes;
        }
        if (_pressId == id)
        {
            EndPress();
        }
        if (_clickId == id)
        {
            _clickId = null;
        }
        outcomes.Add(new(GestureAction.Menu, id));
        return outcomes;
    }

    /// <summary>The keyboard asked for a print's menu at <paramref name="now"/>.</summary>
    public void KeyboardMenuAsked(long now) => _keyboardMenuAt = now;

    /// <summary>A click whose wait is over copies; a press held long enough edits.</summary>
    public IReadOnlyList<GestureOutcome> Due(long now) => Settle(now);

    /// <summary>The board goes away: a waiting click copies now and the press ends, so nothing is left to wait for.</summary>
    public IReadOnlyList<GestureOutcome> Flush(long now)
    {
        var outcomes = Settle(now);
        if (_clickId is { } id)
        {
            _clickId = null;
            outcomes.Add(new(GestureAction.Copy, id));
        }
        EndPress();
        return outcomes;
    }

    /// <summary>Print <paramref name="id"/> left the board: its press ends and its waiting click is dropped.</summary>
    public void Forget(string id)
    {
        if (_pressId == id)
        {
            EndPress();
        }
        if (_clickId == id)
        {
            _clickId = null;
        }
    }

    /// <summary>Ends the press in progress, if any, with nothing coming of it.</summary>
    public void EndPress() => (_pressId, _held, _dragged, _spent) = (null, false, false, false);

    void Begin(string id, double x, double y, long now) =>
        (_pressId, _x, _y, _pressedAt, _held, _dragged, _spent) = (id, x, y, now, false, false, false);

    List<GestureOutcome> Settle(long now)
    {
        List<GestureOutcome> outcomes = [];
        if (_clickId is { } click && now - _clickedAt >= DoubleClickMs)
        {
            _clickId = null;
            outcomes.Add(new(GestureAction.Copy, click));
        }
        if (Sinking && _pressId is { } held && now - _pressedAt >= HoldMs)
        {
            _held = true;
            outcomes.Add(new(GestureAction.Edit, held));
        }
        return outcomes;
    }
}
