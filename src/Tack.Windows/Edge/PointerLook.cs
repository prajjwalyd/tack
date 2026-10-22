using Tack.Core.Diagnostics;
using Tack.Core.Geometry;
using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Windows.Display;
using Tack.Windows.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Tack.Windows.Edge;

/// <summary>
/// One look at the pointer, and what the poller remembers between looks. Runs
/// on the poller's thread only.
/// </summary>
internal sealed class PointerLook(
    Lock gate,
    View view,
    Func<TackSettings> settings,
    Func<bool> snippingToolInFront,
    EdgeStrips? strips)
{
    (bool Left, bool Right) _previous;

    /// <summary>
    /// The left button went down over the board and is still held: a press, a
    /// long press or the start of a drag. Never tuck or flip click-through under it.
    /// </summary>
    bool _pressInside;

    TimeSpan? _awaySince;
    TimeSpan? _lastFullScreenCheck;

    /// <summary>The last look did not watch the buttons.</summary>
    bool _unwatched;

    /// <summary>Looks in a row that could not read the pointer.</summary>
    uint _unreadable;

    /// <summary>What the strips said. Null strips (they could not be made) mean the tucked poller looks slowly.</summary>
    public EdgeWatch Edge { get; } = new();

    /// <summary>One look at the pointer at <paramref name="now"/>, on the monotonic clock.</summary>
    public Decision Tick(TimeSpan now)
    {
        bool shown, edgeReveal;
        lock (gate)
        {
            if (!view.Shown)
            {
                view.DragIn = false;
            }
            (shown, edgeReveal) = (view.Shown, settings().EdgeReveal);
        }
        // The strips are there for a tucked board and edge reveal on, no more.
        if (strips != null)
        {
            strips.Enabled = !shown && edgeReveal;
            Edge.Listen(strips.Enabled);
        }
        if (!shown && !edgeReveal)
        {
            Forget();
            return Decision.Park;
        }

        if (Pointer.Position() is not { } point)
        {
            // Not (0, 0), which would read as the top edge.
            Forget();
            if (!shown && strips != null)
            {
                // The strips say when it is back.
                return Decision.Park;
            }
            _unreadable = _unreadable == uint.MaxValue ? _unreadable : _unreadable + 1;
            return Decision.After(
                _unreadable <= EdgeTiming.UnreadableRetries ? EdgeTiming.PollUnreadable : EdgeTiming.PollUnreadableSlow);
        }
        _unreadable = 0;
        var buttons = (Left: Pointer.KeyDown(VIRTUAL_KEY.VK_LBUTTON), Right: Pointer.KeyDown(VIRTUAL_KEY.VK_RBUTTON));
        if (_unwatched)
        {
            // Buttons held since an unwatched stretch were not pressed just now.
            _unwatched = false;
            _previous = buttons;
        }
        var pressed = (Left: buttons.Left && !_previous.Left, Right: buttons.Right && !_previous.Right);
        _previous = buttons;
        if (!buttons.Left)
        {
            _pressInside = false;
        }

        // Not under the lock: WindowFromPoint may send the window under the
        // pointer a message and wait for its answer.
        var onTaskbar = (pressed.Left || pressed.Right) && Pointer.OnTaskbar(point.X, point.Y);
        Rect? zone = shown ? null : EdgeZone.At(point.X, point.Y);
        var fullScreen = AskFullScreen(now, shown || zone?.Contains(point.X, point.Y) == true);

        lock (gate)
        {
            var current = settings();
            if (fullScreen is { } f)
            {
                view.Fullscreen = f.FullScreen;
                view.Snipping = f.Snipping;
            }
            var decision = view.Shown
                ? Shown(now, point, buttons.Left, pressed, onTaskbar, current)
                : Tucked(now, point, buttons, zone, current);
            if (decision.Action == EdgeAction.Tuck)
            {
                _awaySince = null;
            }
            return decision;
        }
    }

    /// <summary>
    /// Whether a full-screen app is in front, and it is Snipping Tool's own
    /// overlay; null when not asked this time. Only a board that is down (a
    /// full-screen app tucks it) or the pointer at the edge (where it would
    /// reveal) needs to know.
    /// </summary>
    (bool FullScreen, bool Snipping)? AskFullScreen(TimeSpan now, bool wanted)
    {
        if (!wanted)
        {
            // So the edge asks at once when the pointer gets there.
            _lastFullScreenCheck = null;
            return null;
        }
        if (_lastFullScreenCheck is { } last && now - last < EdgeTiming.FullScreenCheck)
        {
            return null;
        }
        _lastFullScreenCheck = now;
        var fullScreen = Monitors.FullScreenAppInFront();
        return (fullScreen, fullScreen && snippingToolInFront());
    }

    Decision Tucked(TimeSpan now, (int X, int Y) point, (bool Left, bool Right) buttons, Rect? zone, TackSettings current)
    {
        var dwell = current.Reveal.Dwell();
        _awaySince = null;
        _pressInside = false;
        view.DragIn = false;
        var atTop = (zone ?? EdgeZone.At(point.X, point.Y)).Contains(point.X, point.Y);
        if (!atTop)
        {
            view.EdgeArmed = true;
        }
        var action = EdgeAction.None;
        if (!current.EdgeReveal || !atTop || !view.EdgeArmed || view.Fullscreen)
        {
            Edge.Reset();
        }
        else
        {
            switch (Edge.Rested(now, point, !buttons.Left && !buttons.Right, dwell))
            {
                case Rest.Edge:
                    action = EdgeAction.Reveal;
                    break;
                case Rest.Drag:
                    view.DragIn = true;
                    action = EdgeAction.Reveal;
                    break;
            }
        }
        if (action == EdgeAction.Reveal)
        {
            return new Decision(action, null, EdgeTiming.Poll);
        }
        var next = Edge.NextLook(now, dwell, view.EdgeArmed, view.Fullscreen);
        return new Decision(action, null, EdgeTiming.TuckedWait(strips != null, next));
    }

    Decision Shown(
        TimeSpan now, (int X, int Y) point, bool left, (bool Left, bool Right) pressed, bool onTaskbar, TackSettings current)
    {
        Edge.Reset();
        if (!left)
        {
            // Dropped (or let go elsewhere): from now on it leaves like any reveal from the edge.
            view.DragIn = false;
        }
        var overBoard = view.Hit(point.X, point.Y);
        var inZone = BoardZone.Leave(view)?.Contains(point.X, point.Y) == true;
        if (inZone)
        {
            view.Entered = true;
            // Visited: from now on it leaves like any other reveal.
            view.StayOpen = false;
        }
        else if ((!view.Entered && view.Reason == RevealReason.Edge) || view.DragIn)
        {
            // Fresh from the edge, or carrying something to the board: the
            // whole strip along the top counts.
            inZone = BoardZone.FreshStrip(view).Contains(point.X, point.Y);
        }

        var held = view.Dragging != null || view.MenuOpen;
        if (pressed.Left && overBoard)
        {
            _pressInside = true;
        }
        // Not the taskbar: a click on the tray icon toggles the board itself
        // when the button comes up, and tucking here on the way down would make
        // it bounce straight back. Nor in Settings, which counts as outside the
        // board but is what holds it down.
        var clickedOutside = (pressed.Left || pressed.Right) && !overBoard && !onTaskbar && !view.SettingsHeld;

        // The window covers the whole work area, and the drag loop drops on
        // whatever window is under the pointer: off the board it must let a
        // print being dragged out through.
        bool? clickThrough = null;
        var follow = view.Dragging != null || (!held && !_pressInside);
        if (follow && overBoard == view.Ignoring)
        {
            view.Ignoring = !overBoard;
            clickThrough = view.Ignoring;
        }

        var peeking = !view.Entered && view.PeekUntil is { } until && now < until;
        // Settings has the focus: the board stays down (no leave, no peek end)
        // for as long as it does.
        var busy = held || _pressInside || view.StayOpen || peeking || view.SettingsHeld;

        var action = EdgeAction.None;
        if (!held && (clickedOutside || BoardZone.FullScreenTucks(view)))
        {
            DevTrace.Write($"tuck: {(clickedOutside ? "a click outside" : "full screen")} "
                + $"(left {pressed.Left}, right {pressed.Right}, at {point.X}, {point.Y})");
            action = EdgeAction.Tuck;
        }
        else if (inZone || busy)
        {
            _awaySince = null;
        }
        else if (now - (_awaySince ??= now) >= current.Reveal.LeaveDelay())
        {
            action = EdgeAction.Tuck;
        }
        return new Decision(action, clickThrough, EdgeTiming.Poll);
    }

    /// <summary>Nothing is being watched: forget what a watch builds up.</summary>
    void Forget()
    {
        Edge.Reset();
        _awaySince = null;
        _pressInside = false;
        // Asked afresh when there is a reason to again.
        _lastFullScreenCheck = null;
        _unwatched = true;
    }
}
