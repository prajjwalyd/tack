using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tack.Core.Diagnostics;
using Tack.Core.Reveal;
using Tack.Core.Settings;

namespace Tack.Windows.Edge;

/// <summary>
/// Decides when the board comes down from the top edge and when it tucks away,
/// and flips the board window's click-through as the pointer crosses the board.
/// Tucked, nothing is polled: the edge strips tell when the pointer reaches the
/// middle of the top edge, or something carried from another app does, and the
/// poller's thread sleeps in between. Shown, it polls rather than hooking the
/// mouse, which would add latency to every app on the system. Wake-ups per second:
/// <list type="bullet">
/// <item>tucked: 0, edge reveal on or off;</item>
/// <item>tucked, the pointer at the edge, until it leaves or the board comes down:
/// 40, fewer than that while the reveal dwell is about to end;</item>
/// <item>tucked, something carried to the edge: 1 or 2, until its dwell ends;</item>
/// <item>tucked just now under the pointer at the edge: 10, until it leaves;</item>
/// <item>shown: 40;</item>
/// <item>tucked, the strips could not be made: 10;</item>
/// <item>the pointer unreadable (a UAC prompt): 1 for three seconds, then 0.1.</item>
/// </list>
/// Every event is raised on the poller's own thread, outside
/// the lock. Click-through is flipped from there on purpose: a drag out blocks
/// the UI thread, and the window must let the drop through all the same.
/// </summary>
/// <param name="gate">The app's lock over the view and the settings; the poller holds it only briefly.</param>
/// <param name="view">The shared view, read and written under <paramref name="gate"/>.</param>
/// <param name="settings">The settings as they are now, read under <paramref name="gate"/>.</param>
/// <param name="snippingToolInFront">Whether the full-screen window in front is Snipping Tool's overlay.</param>
public sealed class EdgePoller(Lock gate, View view, Func<TackSettings> settings, Func<bool> snippingToolInFront)
    : IDisposable
{
    /// <summary>What wakes the poller: a strip's event, or null to look at once.</summary>
    readonly BlockingCollection<EdgeEvent?> _heard = [];
    Thread? _thread;
    bool _disposed;

    /// <summary>The monotonic clock the poller reads <see cref="View.PeekUntil"/> against.</summary>
    public static TimeSpan Now => Stopwatch.GetElapsedTime(0);

    /// <summary>The pointer rested on the top edge: reveal with <see cref="RevealReason.Edge"/>.</summary>
    public event Action? RevealRequested;

    /// <summary>A click outside, a full-screen app, or the pointer gone past the leave delay: tuck.</summary>
    public event Action? TuckRequested;

    /// <summary>Make the board window click-through (true) or not.</summary>
    public event Action<bool>? ClickThroughChanged;

    /// <summary>
    /// The window just became click-through, so the board may never see the
    /// pointer leave a print, which would stay in its hover state.
    /// </summary>
    public event Action? PointerLeft;

    /// <summary>Starts the poller on a thread of its own. Subscribe to the events first.</summary>
    public void Start()
    {
        if (_thread != null)
        {
            return;
        }
        _thread = new Thread(Run) { IsBackground = true, Name = "Tack pointer" };
        _thread.Start();
    }

    /// <summary>
    /// The board just came down, or edge reveal came on or off: the poller looks
    /// at once rather than at its next (perhaps parked) look, so click-through
    /// follows the pointer from the start and the edge strips follow the setting.
    /// Safe from any thread.
    /// </summary>
    public void Wake() => Hear(null);

    void Hear(EdgeEvent? edgeEvent)
    {
        try
        {
            _heard.Add(edgeEvent);
        }
        catch (InvalidOperationException)
        {
            // Closing (or closed): nobody is listening any more.
        }
    }

    void Run()
    {
        EdgeStrips? strips = null;
        try
        {
            strips = EdgeStrips.Start(e => Hear(e));
        }
        catch (ExternalException e)
        {
            DevTrace.Write($"no edge strips ({e.Message}); looking for the top edge by polling");
        }
        try
        {
            Watch(new PointerLook(gate, view, settings, snippingToolInFront, strips));
        }
        catch (Exception e)
        {
            // Never take the whole app down: without the poller the board still
            // opens from the tray and the shortcut.
            DevTrace.Write($"the pointer poller stopped: {e}");
        }
        finally
        {
            strips?.Dispose();
        }
    }

    void Watch(PointerLook look)
    {
        var wait = Apply(look.Tick(Now));
        while (true)
        {
            // Null: nothing to watch until something is heard.
            var timeout = wait is { } pause ? (int)Math.Ceiling(pause.TotalMilliseconds) : Timeout.Infinite;
            if (_heard.TryTake(out var heard, timeout))
            {
                if (heard is { } edgeEvent)
                {
                    look.Edge.Hear(edgeEvent, Now);
                }
            }
            else if (_heard.IsCompleted)
            {
                return;
            }
            wait = Apply(look.Tick(Now));
        }
    }

    TimeSpan? Apply(Decision decision)
    {
        if (decision.ClickThrough is { } ignore)
        {
            ClickThroughChanged?.Invoke(ignore);
            if (ignore)
            {
                PointerLeft?.Invoke();
            }
        }
        switch (decision.Action)
        {
            case EdgeAction.Reveal:
                RevealRequested?.Invoke();
                break;
            case EdgeAction.Tuck:
                TuckRequested?.Invoke();
                break;
        }
        return decision.Wait;
    }

    /// <summary>Stops the poller and takes the strips down.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _heard.CompleteAdding();
        _thread?.Join();
        _heard.Dispose();
    }
}
