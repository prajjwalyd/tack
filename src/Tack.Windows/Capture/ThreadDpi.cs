using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace Tack.Windows.Capture;

/// <summary>
/// Runs code with the calling thread's DPI awareness changed, then puts the
/// previous awareness back. Per-monitor aware, Windows speaks in physical
/// pixels whatever the process's own awareness.
/// </summary>
static class ThreadDpi
{
    public static T PerMonitorAware<T>(Func<T> run) => With(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, run);

    /// <summary>Unaware, where Windows scales coordinates to 96 DPI.</summary>
    public static T Unaware<T>(Func<T> run) => With(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_UNAWARE, run);

    static T With<T>(DPI_AWARENESS_CONTEXT context, Func<T> run)
    {
        var previous = PInvoke.SetThreadDpiAwarenessContext(context);
        try
        {
            return run();
        }
        finally
        {
            if (previous != default)
            {
                PInvoke.SetThreadDpiAwarenessContext(previous);
            }
        }
    }
}
