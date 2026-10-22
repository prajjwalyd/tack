using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Motion;
using Tack.Core.Pinboard.Words;
using Tack.Core.Prints;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard.Feedback;

/// <summary>
/// What Narrator and other screen readers hear when something happens
/// without the focus moving: "Pinned", "Unpinned", "Copied", "Kept", "No
/// longer kept" and every notice. Each is said politely, as a notification
/// from an unseen status line: a burst says only the last, and the same text
/// twice is said twice. The line keeps the words for a few seconds, so a
/// reader browsing later never finds a stale one. While the board is tucked
/// the latest is held and said at the next reveal, if still fresh. UI thread only.
/// </summary>
sealed class Announcer : BoardPart
{
    /// <summary>Lets a burst settle on its last words. Class C.</summary>
    const double SayMs = 40;

    /// <summary>A screen reader needs this long to read it out. Class C.</summary>
    const double KeepMs = 4000;

    /// <summary>Words held while tucked go unsaid after this. Class C.</summary>
    const double StaleMs = 2000;

    const string Activity = "Tack.Board.Announcement";

    readonly TextBlock _line = new() { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false };
    DispatcherQueueTimer? _timer;
    (string Text, long At)? _held;

    public Announcer(BoardContext board) : base(board) => board.Layers.Overlay.Children.Add(_line);

    /// <summary>Says <paramref name="text"/> politely, 40 ms from now; while tucked, at the next reveal.</summary>
    public void Say(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        if (!Board.State.Revealed)
        {
            _held = (text, Environment.TickCount64);
            return;
        }
        Board.Timers.Cancel(_timer);
        _line.Text = string.Empty;
        _timer = Board.Timers.Later(MotionClock.Fixed(SayMs, "a burst of words settles on the last"), () => Speak(text));
    }

    public override void Revealed(RevealReason reason, bool wasRevealed)
    {
        if (_held is { } held)
        {
            _held = null;
            if (Environment.TickCount64 - held.At <= MotionClock.Fixed(StaleMs, "how old held words may be is staleness"))
            {
                Say(held.Text);
            }
        }
    }

    public override void Added(BoardItem item, bool animate, Flight? flight)
    {
        if (animate)
        {
            Say(Labels.Pinned);
        }
    }

    public override void Removed(BoardItem item, Removal how)
    {
        if (how == Removal.Fall && Board.State.Revealed)
        {
            Say(Labels.Unpinned);
        }
    }

    public override void Copied(BoardItem item) => Say(Labels.Copied);

    public override void Updated(BoardItem item, Print old)
    {
        if (item.Print.Kept != old.Kept && Board.State.Revealed)
        {
            Say(Labels.KeptChanged(item.Print.Kept));
        }
    }

    /// <summary>The board tucks: nothing more is said, and the line is cleared.</summary>
    public override void Tucking()
    {
        Board.Timers.Cancel(_timer);
        _timer = null;
        _held = null;
        _line.Text = string.Empty;
    }

    void Speak(string text)
    {
        _line.Text = text;
        var peer = FrameworkElementAutomationPeer.FromElement(_line) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_line);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text, Activity);
        _timer = Board.Timers.Later(MotionClock.Fixed(KeepMs, "a screen reader needs the time to read it out"), () =>
        {
            _timer = null;
            _line.Text = string.Empty;
        });
    }
}
