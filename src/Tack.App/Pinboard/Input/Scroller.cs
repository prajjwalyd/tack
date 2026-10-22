using System.Diagnostics;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tack.Core.Pinboard.Input;
using Tack.Windows.Input;
using Windows.System;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// Scrolling a row wider than the board: the wheel and the touchpad with an
/// exponential approach (<see cref="ScrollModel"/>), the board's own glides to
/// show a print, where the row rests when the board comes down, and the edge
/// fades where more prints wait. Only the track moves, by its translation;
/// nothing is laid out. Frames are asked for only while the row moves, so an
/// idle or tucked board never wakes. UI thread only.
/// </summary>
sealed class Scroller : BoardPart
{
    readonly ScrollModel _model;
    readonly RailFades _fades;
    long _lastFrame;
    bool _framing;

    public Scroller(BoardContext board) : base(board)
    {
        _model = new ScrollModel(board.Clock);
        _fades = new RailFades(board);
        // The felt has no brush of its own, so the board takes the wheel, and drops, between prints too.
        board.Layers.Board.Background ??= new SolidColorBrush(Colors.Transparent);
        board.Layers.Board.PointerWheelChanged += Wheel;
    }

    /// <summary>A scroll starts (the gestures are told first: no hover look until it settles).</summary>
    public event Action? Started;

    /// <summary>The row came to rest or was stopped (the gestures are told first, and hover what is under the pointer).</summary>
    public event Action? Settled;

    /// <summary>Where the row shows now, logical px from its start (also <see cref="Model.BoardState.ScrollPosition"/>).</summary>
    public double Pos => _model.Position;

    /// <summary>Where the row heads: a print is brought into view from here.</summary>
    public double Target => _model.Target;

    /// <summary>The row is moving.</summary>
    public bool Scrolling => _model.Moving;

    bool Glides => Board.State.Revealed && !Board.State.Reduced;

    /// <summary>The row's new extent, after each layout. Scrolled past a shorter row's end, it glides back.</summary>
    public void SetExtent(double max) => Change(() => _model.SetExtent(max, Glides));

    /// <summary>Scrolls to <paramref name="x"/> (kept within the row): a glide, or a jump if <paramref name="instant"/>, tucked or under reduced motion.</summary>
    public void ScrollTo(double x, bool instant = false) => Change(() => _model.ScrollTo(x, instant || !Glides));

    /// <summary>Puts the row where it rests for the next reveal: its start, or the first print that is not kept.</summary>
    public void Rest() => Change(() => _model.ScrollTo(Board.Parts.Row.RestScroll(), instant: true));

    /// <summary>Stops dead where it shows.</summary>
    public void Halt() => Change(_model.Halt);

    public override void Synced() => Rest();

    public override void Tucking() => Halt();

    public override void Tucked() => Rest();

    void Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            // Never a zoom.
            e.Handled = true;
            return;
        }
        var point = e.GetCurrentPoint(Board.Layers.Board);
        var horizontal = point.Properties.IsHorizontalMouseWheel;
        var turn = ScrollWheel.FromWindows(
            point.Properties.MouseWheelDelta,
            horizontal,
            WheelSettings.Lines(horizontal),
            touchpad: point.PointerDeviceType == PointerDeviceType.Touchpad,
            pageWidth: Board.State.BoardWidth);
        var taken = false;
        Change(() => taken = _model.Wheel(turn, Board.State.Revealed, Board.State.Reduced));
        e.Handled = taken;
    }

    /// <summary>Runs <paramref name="change"/> on the model, then shows the result and starts or stops the frames.</summary>
    void Change(Action change)
    {
        var was = _model.Moving;
        change();
        Apply();
        if (!was && _model.Moving)
        {
            _lastFrame = Stopwatch.GetTimestamp();
            Frames(true);
            Board.Parts.Gestures.ScrollChanged(true);
            Started?.Invoke();
        }
        else if (was && !_model.Moving)
        {
            Frames(false);
            Board.Parts.Gestures.ScrollChanged(false);
            Board.Parts.Keyboard.Rehover();
            Settled?.Invoke();
        }
    }

    void Frame(object? sender, object e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds;
        _lastFrame = now;
        Change(() => _model.Step(dt));
    }

    void Frames(bool on)
    {
        if (on == _framing)
        {
            return;
        }
        _framing = on;
        if (on)
        {
            CompositionTarget.Rendering += Frame;
        }
        else
        {
            CompositionTarget.Rendering -= Frame;
        }
    }

    /// <summary>Moves the track to the position shown and lets the edge fades follow.</summary>
    void Apply()
    {
        var pos = _model.Position;
        Board.State.ScrollPosition = pos;
        var track = Board.Layers.Track;
        var translation = new Vector3((float)-pos, 0, 0);
        if (track.Translation != translation)
        {
            track.Translation = translation;
        }
        _fades.Follow(_model.Overflows, _model.Edges);
    }
}
