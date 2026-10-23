using Tack.Core.Diagnostics;
using Windows.Win32;

namespace Tack.Windows.Host;

/// <summary>
/// Signing out, restarting and shutting down. Windows asks every top-level
/// window first (WM_QUERYENDSESSION), then says whether the session really
/// ends (WM_ENDSESSION); once that has been answered, the process may be ended
/// at any moment, without the app's own quit ever running.
/// </summary>
public static class SessionEnd
{
    /// <summary>
    /// Never stands in the way of the session ending, and calls
    /// <paramref name="ending"/> on the UI thread when it really ends, before
    /// Windows ends Tack: the place to write what must not be lost.
    /// </summary>
    public static void Follow(HostWindow host, Action ending)
    {
        host.Answer(PInvoke.WM_QUERYENDSESSION, (_, _) => 1);
        host.Listen(PInvoke.WM_ENDSESSION, (wParam, _) =>
        {
            // FALSE: someone else cancelled it, and Tack carries on.
            if (wParam != 0)
            {
                DevTrace.Write("session: Windows is signing out or shutting down");
                ending();
            }
        });
    }
}
