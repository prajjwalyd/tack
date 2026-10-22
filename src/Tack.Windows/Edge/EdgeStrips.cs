using Windows.Win32;

namespace Tack.Windows.Edge;

/// <summary>
/// Tells when the pointer reaches the top edge, without anyone looking: one
/// thin strip window along the top of each monitor, in the middle third of it
/// (<see cref="EdgeZone"/>), near enough invisible. A strip is sent a mouse
/// move only when the pointer is on it, and an OLE drag over it is a drop
/// target's call. A thread of its own sleeps in GetMessage between those:
/// 0 wake-ups per second.
/// <para>
/// The strips are topmost when made. A topmost window raised over the zone
/// afterwards (a full-screen player, say) hides it until the strips are made
/// again, which is at every tuck: they exist only while the board is tucked
/// and edge reveal is on (<see cref="Enabled"/>).
/// </para>
/// </summary>
internal sealed class EdgeStrips : IDisposable
{
    readonly Thread _thread;
    readonly uint _threadId;
    bool _enabled;
    bool _disposed;

    EdgeStrips(Thread thread, uint threadId) => (_thread, _threadId) = (thread, threadId);

    /// <summary>
    /// Starts the strips' thread, with no strip yet (<see cref="Enabled"/>).
    /// <paramref name="onEvent"/> runs on that thread, and must not block.
    /// Throws an <see cref="System.Runtime.InteropServices.ExternalException"/>
    /// when the thread cannot set up OLE or its window class.
    /// </summary>
    public static EdgeStrips Start(Action<EdgeEvent> onEvent)
    {
        var started = new TaskCompletionSource<uint>();
        var thread = new Thread(() => StripThread.Run(onEvent, started)) { IsBackground = true, Name = "Tack edge" };
        // OLE drag and drop needs a single-threaded apartment.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return new EdgeStrips(thread, started.Task.GetAwaiter().GetResult());
    }

    /// <summary>The strips exist (one per monitor) or do not.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value != _enabled)
            {
                _enabled = value;
                PInvoke.PostThreadMessage(_threadId, StripThread.EnableMessage, value ? 1u : 0u, 0);
            }
        }
    }

    /// <summary>Takes the strips down and ends their thread.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join();
    }
}
