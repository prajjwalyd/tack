using Tack.Core.Diagnostics;

namespace Tack.Core.Phone;

/// <summary>
/// Runs a piece of work on the thread pool, one run at a time. Asking while it
/// runs, once or many times, makes it run once more when it is done, so the
/// last ask is always followed by a whole run and nothing piles up. A run that
/// fails is traced and does not stop the next. Safe on any thread.
/// </summary>
/// <param name="what">Names the work in the trace.</param>
public sealed class OneAtATime(string what, Func<Task> work)
{
    readonly Lock _lock = new();
    bool _running;
    bool _again;

    /// <summary>Runs the work now, or once more after the run under way.</summary>
    public void Nudge()
    {
        lock (_lock)
        {
            if (_running)
            {
                _again = true;
                return;
            }
            _running = true;
        }
        _ = Task.Run(RunAsync);
    }

    async Task RunAsync()
    {
        while (true)
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                DevTrace.Write($"{what} failed: {e}");
            }
            lock (_lock)
            {
                if (!_again)
                {
                    _running = false;
                    return;
                }
                _again = false;
            }
        }
    }
}
