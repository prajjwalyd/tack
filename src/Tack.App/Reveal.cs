using Microsoft.UI.Dispatching;
using Tack.App.Pinboard;
using Tack.App.Pinboard.Contract;
using Tack.Core.Diagnostics;
using Tack.Core.Reveal;
using Tack.Windows.Display;
using Tack.Windows.Edge;
using Tack.Windows.Snips;

namespace Tack.App;

/// <summary>
/// Bringing the board down and tucking it away: the shared view changes under
/// the app's lock, and the window follows on the UI thread. A request from
/// another thread is posted there, and each one checks the view's generation
/// first, so one overtaken by a later reveal or tuck does nothing. The board
/// hears of each through the feed, raised from the UI thread once the window
/// is in place, so the board's events keep the order of the window's moves.
/// </summary>
sealed class Reveal
{
    readonly AppState _state;
    readonly DispatcherQueue _ui;
    readonly BoardWindow _board;
    readonly BoardFeed _feed;
    readonly Action _catchUp;
    readonly ClickThrough _clickThrough;
    readonly FullScreenCheck _fullScreen;
    EdgePoller? _poller;

    /// <param name="catchUp">What may have changed while the board was away, looked at once it is down; on the thread pool.</param>
    public Reveal(AppState state, DispatcherQueue ui, BoardWindow board, BoardFeed feed, Action catchUp)
    {
        (_state, _ui, _board, _feed, _catchUp) = (state, ui, board, feed, catchUp);
        _clickThrough = new ClickThrough(board.Handle);
        _fullScreen = new FullScreenCheck(state);
        Focus = new BoardFocus(board.Handle);
    }

    /// <summary>The keyboard: the board takes it for a shortcut reveal and gives it back on every tuck.</summary>
    public BoardFocus Focus { get; }

    /// <summary>The edge poller, woken on each reveal so it starts following the pointer.</summary>
    public void Attach(EdgePoller poller) => _poller = poller;

    /// <summary>The show-or-hide shortcut or a click on the tray icon: down if it is up, up if it is down.</summary>
    public void Toggle(RevealReason reason)
    {
        bool shown;
        lock (_state.Gate)
        {
            shown = _state.View.Shown;
        }
        if (shown)
        {
            Tuck();
        }
        else
        {
            Show(reason);
        }
    }

    /// <summary>The edge poller's settings changed (edge reveal on or off): it looks at them again.</summary>
    public void WakePoller() => _poller?.Wake();

    /// <summary>
    /// Whether a new print or a notice may bring the board down now: nothing
    /// full screen holds it back. May take up to a second; off the UI thread.
    /// </summary>
    public async Task<bool> MayRevealNewAsync() => !await _fullScreen.HoldsBackAsync().ConfigureAwait(false);

    /// <summary>
    /// A new print is up: the board peeks out to show it, unless something is
    /// full screen. Finishes once the reveal is on its way (see
    /// <see cref="Prints.AnnounceLater"/>); off the UI thread.
    /// </summary>
    public async Task ForNewAsync()
    {
        if (await MayRevealNewAsync().ConfigureAwait(false))
        {
            Show(RevealReason.New, Placement.UnderPointer(), Speed().Peek());
        }
        // Otherwise the print is pinned all the same, and shows at the next reveal.
    }

    /// <summary>A new capture flies onto the board, which comes down at <paramref name="place"/>, where it was taken.</summary>
    public void FlyIn(Placement place) => Show(RevealReason.New, place, Speed().FlightPeek());

    /// <summary>A notice is to be shown: the board comes down briefly under the pointer (the full-screen check is the caller's).</summary>
    public void ForNotice() => Show(RevealReason.New, Placement.UnderPointer(), Speed().NoticePeek());

    /// <summary>Brings the board down on the monitor under the pointer, from any thread.</summary>
    public void Show(RevealReason reason) => Show(reason, Placement.UnderPointer(), TimeSpan.Zero);

    /// <summary>
    /// Brings the board down at <paramref name="place"/>, with no full-screen check.
    /// For a new print, <paramref name="peek"/> is how long it stays down on its own.
    /// A board already down on another monitor moves to <paramref name="place"/>.
    /// </summary>
    public void Show(RevealReason reason, Placement place, TimeSpan peek)
    {
        ulong generation;
        double motion;
        lock (_state.Gate)
        {
            var view = _state.View;
            motion = _state.Settings.Reveal.MotionScale();
            // Explicit requests always win; the edge does not interrupt a full-screen app.
            if (reason == RevealReason.Edge && view.Fullscreen)
            {
                return;
            }
            if (reason is RevealReason.Hotkey or RevealReason.Tray)
            {
                view.StayOpen = true;
            }
            else if (reason == RevealReason.New && EdgePoller.Now + peek is var until && (view.PeekUntil ?? TimeSpan.Zero) < until)
            {
                // Never cuts short a longer peek already running.
                view.PeekUntil = until;
            }
            if (view.Shown)
            {
                if (reason == RevealReason.New && (view.WindowPos != place.Position || view.WindowSize != place.Size))
                {
                    MoveTo(view, place);
                }
                return;
            }
            view.Shown = true;
            view.Generation++;
            view.Reason = reason;
            Place(view, place);
            view.Ignoring = true;
            generation = view.Generation;
        }
        DevTrace.Write($"reveal: {reason}, generation {generation}");
        _clickThrough.Set(true);
        _poller?.Wake();
        _ui.TryEnqueue(() =>
        {
            if (!Current(generation))
            {
                return;
            }
            _board.ShowAt(place);
            _clickThrough.Apply();
            _feed.RaiseRevealed(reason, motion);
            // After the reveal is told, so prints that aged out meanwhile fall from a board that is down.
            Background.Run("catching up", _catchUp);
        });
    }

    /// <summary>
    /// Sends the board up, and hides the window once the swing is over. Any tuck
    /// ends the Settings hold: it is the user's own say or a full-screen app's.
    /// </summary>
    public void Tuck()
    {
        ulong generation;
        TimeSpan hideAfter;
        lock (_state.Gate)
        {
            hideAfter = _state.Settings.Reveal.TuckHideDelay();
            if (_state.View.Tuck() is not { } tucked)
            {
                return;
            }
            generation = tucked;
        }
        DevTrace.Write($"reveal: tuck, generation {generation}");
        _ui.TryEnqueue(() =>
        {
            Focus.GiveBack();
            _feed.RaiseTucking();
        });
        Background.Run("hiding the board", async () =>
        {
            await Task.Delay(hideAfter).ConfigureAwait(false);
            _ui.TryEnqueue(() =>
            {
                lock (_state.Gate)
                {
                    if (_state.View.Generation != generation || _state.View.Shown)
                    {
                        return;
                    }
                }
                _board.Hide();
            });
        });
    }

    /// <summary>
    /// Settings holds the board down while it has the keyboard focus, so a change
    /// shows live; holding brings a tucked board down unless something is full screen.
    /// </summary>
    public void HoldForSettings(bool on)
    {
        lock (_state.Gate)
        {
            if (!_state.View.HoldForSettings(on))
            {
                return;
            }
        }
        if (Monitors.FullScreenAppInFront() && !SnippingTool.InFront())
        {
            lock (_state.Gate)
            {
                _state.View.SettingsHeld = false;
            }
            return;
        }
        Show(RevealReason.Settings);
    }

    /// <summary>The edge poller's decision, applied on its own thread (see <see cref="ClickThrough"/>).</summary>
    public void SetClickThrough(bool ignore) => _clickThrough.Set(ignore);

    /// <summary>Down on another monitor than a new print's: the board moves there, as it is.</summary>
    void MoveTo(View view, Placement place)
    {
        Place(view, place);
        var generation = view.Generation;
        _ui.TryEnqueue(() =>
        {
            if (Current(generation))
            {
                _board.MoveTo(place);
                _clickThrough.Apply();
            }
        });
    }

    static void Place(View view, Placement place)
    {
        view.Entered = false;
        view.WindowPos = place.Position;
        view.WindowSize = place.Size;
        view.MonitorTop = place.MonitorTop;
        view.Scale = place.Scale;
    }

    bool Current(ulong generation)
    {
        lock (_state.Gate)
        {
            return _state.View.Generation == generation;
        }
    }

    RevealSpeed Speed()
    {
        lock (_state.Gate)
        {
            return _state.Settings.Reveal;
        }
    }
}
