using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Edge;

/// <summary>
/// The board's window as an overlay: transparent and topmost across one
/// monitor's work area, never activated, and click-through except where the
/// board is. Plain Win32 calls rather than the UI framework's setters, which
/// rewrite the whole extended style (and re-show, activating, the window)
/// whenever one of their own flags changes.
/// </summary>
public static class Overlay
{
    /// <summary>Once at startup: the window never takes focus and never shows up in Alt+Tab or the taskbar.</summary>
    public static void MakeUnfocusable(nint hwnd) =>
        ExStyle.Set((HWND)hwnd, WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, true);

    /// <summary>
    /// Turns mouse pass-through on or off. The window is as wide as the monitor
    /// and the board much narrower, so clicks are let through to the apps below
    /// except while the pointer is on the board. The edge poller calls this from
    /// its own thread.
    /// </summary>
    public static void SetClickThrough(nint hwnd, bool ignore) =>
        ExStyle.Set((HWND)hwnd, WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_LAYERED, ignore);

    /// <summary>
    /// Shows the window, click-through and without activating it, with its
    /// top-left at <paramref name="position"/> and <paramref name="size"/>
    /// (physical px). Call it on the window's own thread.
    /// </summary>
    public static void ShowAt(nint hwnd, (int X, int Y) position, (int W, int H) size)
    {
        var window = (HWND)hwnd;
        SetClickThrough(hwnd, true);
        // Move first, then size: crossing to a monitor with another DPI makes
        // the UI framework resize the window on WM_DPICHANGED, and the second
        // call puts the size right in the new monitor's pixels.
        PInvoke.SetWindowPos(
            window, HWND.HWND_TOPMOST, position.X, position.Y, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        PInvoke.SetWindowPos(
            window, HWND.HWND_TOPMOST, position.X, position.Y, size.W, size.H,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    /// <summary>Hides the window. Call it on the window's own thread.</summary>
    public static void Hide(nint hwnd) => PInvoke.ShowWindow((HWND)hwnd, SHOW_WINDOW_CMD.SW_HIDE);
}
