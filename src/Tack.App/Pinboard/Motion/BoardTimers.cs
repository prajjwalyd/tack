using Microsoft.UI.Dispatching;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// One-shot timers for the board's choreography (a pin-on after the swing, a
/// tock as the pin lands, a badge going away), on the UI thread. The board
/// cancels them all when it tucks, so nothing wakes while it is away.
/// </summary>
sealed class BoardTimers(DispatcherQueue queue)
{
    readonly HashSet<DispatcherQueueTimer> _pending = [];

    /// <summary>Timers waiting to run.</summary>
    public int Pending => _pending.Count;

    /// <summary>Runs <paramref name="run"/> once, <paramref name="ms"/> from now (already scaled for the speed if it is class A).</summary>
    public DispatcherQueueTimer Later(double ms, Action run)
    {
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            if (_pending.Remove(t))
            {
                run();
            }
        };
        _pending.Add(timer);
        timer.Start();
        return timer;
    }

    /// <summary>Stops <paramref name="timer"/> if it has not run; null or a timer already run does nothing.</summary>
    public void Cancel(DispatcherQueueTimer? timer)
    {
        if (timer is not null && _pending.Remove(timer))
        {
            timer.Stop();
        }
    }

    public void CancelAll()
    {
        foreach (var timer in _pending)
        {
            timer.Stop();
        }
        _pending.Clear();
    }
}
