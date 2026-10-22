using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Geometry;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Layout;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard;

/// <summary>
/// The board's width and the rect where the window takes the pointer. The
/// rect is the board plus <see cref="BoardMetrics.Lift"/> on each side, within
/// the window, from the screen edge down to the board's bottom plus the lift:
/// room for a hovered print to rise. Physical px relative to the window, sent
/// to the app only when it changes.
/// </summary>
/// <remarks>
/// While the board is down, a burst of layouts sends one rect after
/// <see cref="BoardMetrics.RectCoalesceMs"/>; while the cork slides to a new
/// width the rect covers the wider of the old and new, and shrinks once the
/// slide is over. While tucked, the rect goes out at once, with no timers.
/// </remarks>
sealed class BoardGeometry(BoardContext board)
{
    /// <summary>After the cork's slide, a margin of frames before the rect shrinks. Class C.</summary>
    const double SettleMarginMs = 40;

    Rect? _sent;
    DispatcherQueueTimer? _coalesce;
    DispatcherQueueTimer? _settle;

    /// <summary>The width the rect still covers while the cork slides from it.</summary>
    double _covered;

    /// <summary>The board's width, logical px.</summary>
    public double Width { get; private set; }

    /// <summary>Sizes the board to <paramref name="width"/>, keeps the row centred on it and aims the hinge. The rect follows through <see cref="SendRect"/>.</summary>
    public void SetWidth(double width)
    {
        Width = width;
        board.State.BoardWidth = width;
        var layers = board.Layers;
        layers.Stage.Width = layers.Board.Width = layers.Cork.Width = layers.Rail.Width = layers.Veil.Width = width;
        Canvas.SetLeft(layers.Track, width / 2);
        board.Hinge.Aim(width);
    }

    /// <summary>
    /// The rect for the board's width, after a layout. <paramref name="instant"/>:
    /// the width did not slide, so the rect need not cover the old one.
    /// </summary>
    public void SendRect(bool instant)
    {
        if (!board.State.Revealed)
        {
            StopRectTimers();
            return;
        }
        board.Timers.Cancel(_coalesce);
        _coalesce = board.Timers.Later(MotionClock.Fixed(BoardMetrics.RectCoalesceMs, "one rect per frame for a burst of layouts"), () =>
        {
            _coalesce = null;
            if (instant)
            {
                _covered = Width;
            }
            PushRect(Math.Max(_covered, Width));
            board.Timers.Cancel(_settle);
            var settle = board.Clock.LayoutMs + MotionClock.Fixed(SettleMarginMs, "a margin of frames after the slide");
            _settle = board.Timers.Later(settle, () =>
            {
                _settle = null;
                _covered = Width;
                PushRect(Width);
            });
        });
    }

    /// <summary>Drops the rect's pending updates and sends the rect for the board's width now (a tuck, a layout while tucked).</summary>
    public void StopRectTimers()
    {
        board.Timers.Cancel(_coalesce);
        board.Timers.Cancel(_settle);
        (_coalesce, _settle) = (null, null);
        _covered = Width;
        PushRect(Width);
    }

    /// <summary>Sends the rect for the board's width now, unless it is the one last sent.</summary>
    public void PushRect() => PushRect(Width);

    /// <summary>
    /// Sends the rect for a board <paramref name="width"/> wide, which may be
    /// wider than it is (while it shrinks, the rect can cover the old width).
    /// </summary>
    public void PushRect(double width)
    {
        var state = board.State;
        if (state.WindowWidth <= 0 || width <= 0)
        {
            return;
        }
        var rect = RowLayout.BoardRect(state.WindowWidth, width, state.Scale);
        if (rect == _sent)
        {
            return;
        }
        _sent = rect;
        board.Actions.SetBoardRect(rect);
    }

    /// <summary>Forgets the rect sent, so the next one goes out even if unchanged (a reveal, a new monitor).</summary>
    public void ForgetRect() => _sent = null;
}
