using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// The breeze: every 12 to 25 seconds while the board is down, a faint push
/// ripples along the prints in view. Not under reduced motion, nor while a
/// print is pressed or dragged; the hovered print and prints waiting to pin
/// on are left alone. Its timers run only while the board is down. UI thread only.
/// </summary>
sealed class Gusts(BoardContext board, Swings swings)
{
    readonly List<DispatcherQueueTimer> _pushes = [];
    DispatcherQueueTimer? _next;

    BoardState State => board.State;

    /// <summary>Plans the next breeze, if the board is down and full motion is wanted; any earlier plan is dropped.</summary>
    public void Start()
    {
        board.Timers.Cancel(_next);
        _next = null;
        if (!State.Revealed || State.Reduced)
        {
            return;
        }
        _next = board.Timers.Later(Breeze.NextMs(board.Random.NextDouble), () =>
        {
            _next = null;
            Blow();
            Start();
        });
    }

    /// <summary>No more breezes, and a ripple under way stops where it is.</summary>
    public void Stop()
    {
        board.Timers.Cancel(_next);
        _next = null;
        CancelPushes();
    }

    void CancelPushes()
    {
        foreach (var timer in _pushes)
        {
            board.Timers.Cancel(timer);
        }
        _pushes.Clear();
    }

    bool Busy => State.Pressing is not null || State.DraggingId is not null;

    void Blow()
    {
        if (!State.Revealed || State.Reduced || Busy)
        {
            return;
        }
        // The last ripple ended long ago: a breeze is over within a second.
        CancelPushes();
        var row = board.Parts.Row;
        var layout = row.Current;
        var live = State.Row
            .Select((item, index) => (item, index))
            .Where(p => p.index < layout.Count && layout.InView(p.index, State.ScrollPosition))
            .Select(p => p.item)
            .Where(item => !item.Leaving && !row.IsHidden(item) && !State.Awaiting.Contains(item))
            .ToList();
        foreach (var push in Breeze.Plan(live.Count, board.Clock, board.Random.NextDouble))
        {
            var item = live[push.Index];
            _pushes.Add(board.Timers.Later(push.DelayMs, () =>
            {
                if (!Busy && item != State.Hovered)
                {
                    swings.Nudge(item, push.Amplitude);
                }
            }));
        }
    }
}
