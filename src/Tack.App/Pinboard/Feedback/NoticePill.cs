using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Motion;
using Tack.Core.Pinboard.Motion;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard.Feedback;

/// <summary>
/// The short notice under the board ("Already pinned"...), also said aloud.
/// It eases in, stays up for its reading time, and fades out. A notice that
/// arrives while the board is tucked is held and shown once the next reveal
/// is at rest, unless it is older than two seconds by then; the tuck takes a
/// shown one away at once. UI thread only.
/// </summary>
sealed class NoticePill(BoardContext board) : BoardPart(board)
{
    /// <summary>Reading time for the words on it. Class D.</summary>
    const double ShowMs = 1800;

    /// <summary>A held notice older than this at the reveal is dropped. Class C.</summary>
    const double StaleMs = 2000;

    const double InMs = 200;
    const double ReducedInMs = 120;
    const double OutMs = 240;

    NoticeLook? _look;
    (string Text, long At)? _held;
    DispatcherQueueTimer? _showTimer;
    DispatcherQueueTimer? _hideTimer;
    Playing? _motion;

    /// <summary>The words on the pill now, or null when it is not up.</summary>
    public string? Showing { get; private set; }

    public override void Notice(string text)
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
        Show(text);
    }

    public override void Revealed(RevealReason reason, bool wasRevealed)
    {
        if (_held is not { } held)
        {
            return;
        }
        _held = null;
        if (Environment.TickCount64 - held.At > MotionClock.Fixed(StaleMs, "how old a held notice may be is staleness, not motion"))
        {
            return;
        }
        if (wasRevealed)
        {
            Show(held.Text);
            return;
        }
        Board.Timers.Cancel(_showTimer);
        _showTimer = Board.Timers.Later(Board.Clock.RestMs(Board.State.Reduced), () =>
        {
            _showTimer = null;
            Show(held.Text);
        });
    }

    /// <summary>The tuck: away at once, and nothing held.</summary>
    public override void Tucking()
    {
        _held = null;
        Board.Timers.Cancel(_showTimer);
        Board.Timers.Cancel(_hideTimer);
        (_showTimer, _hideTimer) = (null, null);
        _motion?.Cancel();
        _motion = null;
        _look?.Hide();
        Showing = null;
    }

    public override void ThemeChanged() => _look?.ApplyTheme();

    void Show(string text)
    {
        Board.Parts.Announcer.Say(text);
        var look = Look();
        Board.Timers.Cancel(_hideTimer);
        _motion?.Cancel();
        look.Show(text);
        Showing = text;
        _motion = Board.Keyframer.Play(In(), look.Visual);
        _hideTimer = Board.Timers.Later(MotionClock.Fixed(ShowMs, "reading time for the text on it"), () =>
        {
            _hideTimer = null;
            _motion = Board.Keyframer.Play(Out(), look.Visual, () =>
            {
                _motion = null;
                look.Hide();
                Showing = null;
            });
        });
    }

    /// <summary>Eases in from a little higher and smaller; under reduced motion, only fades in.</summary>
    SampledMotion In()
    {
        var reduced = Board.State.Reduced;
        var timeline = new Timeline(Board.Clock.Scaled(reduced ? ReducedInMs : InMs), easing: CubicBezier.QuickOut, fill: Fill.Forwards);
        return reduced
            ? timeline.Key(0, new Pose { Opacity = 0 }).Key(1, new Pose { Opacity = 1 }).Sample()
            : timeline.Key(0, new Pose { Opacity = 0, Y = -4, Scale = 0.98 }).Key(1, Pose.None with { Opacity = 1 }).Sample();
    }

    SampledMotion Out() =>
        new Timeline(Board.Clock.Scaled(OutMs), easing: CubicBezier.EaseIn, fill: Fill.Forwards)
            .Key(0, new Pose { Opacity = 1 })
            .Key(1, new Pose { Opacity = 0 })
            .Sample();

    /// <summary>The pill, made at the first notice.</summary>
    NoticeLook Look()
    {
        return _look ??= new NoticeLook(Board.Theme, Board.Layers.Overlay);
    }
}
