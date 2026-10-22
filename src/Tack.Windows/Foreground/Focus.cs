using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Foreground;

/// <summary>
/// Keyboard focus for the board, only when the keyboard asked for it. The
/// board is normally WS_EX_NOACTIVATE, so the edge, a new capture or a click
/// never pulls the keyboard away from the app in use. The show-or-hide
/// shortcut makes it activatable and brings it to the front (<see cref="Take"/>);
/// when it goes, the previous window gets the keyboard back
/// (<see cref="GiveBack"/>). Both run on the UI thread.
/// </summary>
public static unsafe class Focus
{
    const int NoActivate = (int)WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

    /// <summary>
    /// Makes <paramref name="window"/> the foreground window. Returns the window
    /// that had the foreground before (to give it back later), or null if
    /// <paramref name="window"/> could not take it.
    /// </summary>
    public static nint? Take(nint window)
    {
        var hwnd = (HWND)window;
        var previous = PInvoke.GetForegroundWindow();
        SetNoActivate(hwnd, false);
        if (previous == hwnd)
        {
            return window;
        }
        ForceForeground(hwnd);
        if (PInvoke.GetForegroundWindow() == hwnd)
        {
            return (nint)previous.Value;
        }
        SetNoActivate(hwnd, true);
        return null;
    }

    /// <summary>
    /// Gives the foreground back to <paramref name="previous"/> if the board
    /// still has it (if the user clicked another window meanwhile, that one
    /// keeps it), and makes the board unfocusable again.
    /// </summary>
    public static void GiveBack(nint window, nint? previous)
    {
        var hwnd = (HWND)window;
        if (PInvoke.GetForegroundWindow() == hwnd && previous is { } p && p != window && p != 0)
        {
            var back = (HWND)p;
            if (PInvoke.IsWindow(back) && PInvoke.IsWindowVisible(back))
            {
                ForceForeground(back);
            }
        }
        SetNoActivate(hwnd, true);
    }

    /// <summary>The process whose window is in front.</summary>
    public static uint? ForegroundProcess() => WindowFacts.ProcessId(PInvoke.GetForegroundWindow());

    /// <summary>
    /// For tests and the debug harness: brings the main window of process
    /// <paramref name="pid"/> to the front. Returns the window that was in front
    /// before, or null if that process's window did not get the foreground.
    /// </summary>
    public static nint? BringToFront(uint pid)
    {
        var previous = PInvoke.GetForegroundWindow();
        var search = (Pid: pid, Found: HWND.Null);
        PInvoke.EnumWindows(&FindMainWindow, (LPARAM)(nint)(&search));
        if (search.Found.IsNull)
        {
            return null;
        }
        ForceForeground(search.Found);
        return ForegroundProcess() == pid ? (nint)previous.Value : null;
    }

    /// <summary>Gives the foreground back to a window <see cref="BringToFront"/> took it from.</summary>
    public static void ReturnForeground(nint previous)
    {
        var hwnd = (HWND)previous;
        if (!hwnd.IsNull && PInvoke.IsWindow(hwnd))
        {
            ForceForeground(hwnd);
        }
    }

    /// <summary>
    /// Brings a window to the foreground. Windows only lets the app the user is
    /// working with change the foreground; sharing that app's input state for a
    /// moment counts as being it, with no key pressed on anyone's behalf.
    /// </summary>
    static void ForceForeground(HWND hwnd)
    {
        if (PInvoke.SetForegroundWindow(hwnd))
        {
            return;
        }
        // AttachThreadInput needs this thread to have a message queue; asking
        // for a message makes one if it has none yet.
        MSG message;
        PInvoke.PeekMessage(&message, HWND.Null, PInvoke.WM_USER, PInvoke.WM_USER, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        var ours = PInvoke.GetCurrentThreadId();
        var theirs = PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), null);
        var attached = theirs != 0 && theirs != ours && PInvoke.AttachThreadInput(ours, theirs, true);
        PInvoke.BringWindowToTop(hwnd);
        PInvoke.SetForegroundWindow(hwnd);
        if (attached)
        {
            PInvoke.AttachThreadInput(ours, theirs, false);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static BOOL FindMainWindow(HWND hwnd, LPARAM data)
    {
        var search = (ValueTuple<uint, HWND>*)data.Value;
        uint pid = 0;
        PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        var topLevel = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER).IsNull;
        if (pid == search->Item1 && PInvoke.IsWindowVisible(hwnd) && topLevel)
        {
            search->Item2 = hwnd;
            return false;
        }
        return true;
    }

    static void SetNoActivate(HWND hwnd, bool on)
    {
        var style = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var changed = on ? style | NoActivate : style & ~(nint)NoActivate;
        if (changed != style)
        {
            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, changed);
        }
    }
}
