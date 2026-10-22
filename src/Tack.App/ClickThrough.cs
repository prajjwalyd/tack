using Tack.Windows.Edge;

namespace Tack.App;

/// <summary>
/// Whether the board's window lets the pointer through, as last decided. The
/// edge poller decides on its own thread, and the change is applied right
/// there rather than queued for the UI thread: a drag out runs inside a UI
/// thread callback, so anything queued would land after the drop, and the
/// drop would hit the board instead of the app below.
/// </summary>
sealed class ClickThrough(nint window)
{
    int _ignoring = 1;

    /// <summary>Decides, and brings the window in line.</summary>
    public void Set(bool ignore)
    {
        Volatile.Write(ref _ignoring, ignore ? 1 : 0);
        Apply();
    }

    /// <summary>
    /// Brings the window in line with the last decision. Showing the window
    /// makes it click-through while the poller may decide otherwise on another
    /// thread, so this applies until the decision holds still.
    /// </summary>
    public void Apply()
    {
        while (true)
        {
            var ignore = Volatile.Read(ref _ignoring) == 1;
            Overlay.SetClickThrough(window, ignore);
            if ((Volatile.Read(ref _ignoring) == 1) == ignore)
            {
                return;
            }
        }
    }
}
