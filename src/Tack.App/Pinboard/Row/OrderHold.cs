using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// Holds the app's new row orders back for a while, so a keep press is seen
/// before the print moves; the last order held is applied when the hold ends,
/// or at once on a tuck (<see cref="Flush"/>), so no timer outlives the board.
/// </summary>
sealed class OrderHold(BoardContext board, Action<IReadOnlyList<string>> apply)
{
    long _until;
    IReadOnlyList<string>? _pending;
    DispatcherQueueTimer? _timer;

    /// <summary>Holds orders back for <paramref name="ms"/> from now (already scaled), or longer if already held.</summary>
    public void Hold(double ms) =>
        _until = Math.Max(_until, Stopwatch.GetTimestamp() + (long)(ms * Stopwatch.Frequency / 1000));

    /// <summary>Applies <paramref name="ids"/> now, or once the hold is over if the board is down and held.</summary>
    public void Offer(IReadOnlyList<string> ids)
    {
        var wait = (_until - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
        if (wait > 0 && board.State.Revealed)
        {
            _pending = ids;
            _timer ??= board.Timers.Later(wait, () =>
            {
                _timer = null;
                Release();
            });
            return;
        }
        apply(ids);
    }

    /// <summary>Ends the hold and applies an order still waiting (a tuck).</summary>
    public void Flush()
    {
        _until = 0;
        board.Timers.Cancel(_timer);
        _timer = null;
        Release();
    }

    void Release()
    {
        if (_pending is { } ids)
        {
            _pending = null;
            Offer(ids);
        }
    }
}
