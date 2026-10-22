using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Input;

/// <summary>
/// Where the pointer is and what is under it, read straight from Windows. The
/// board window is click-through most of the time, so it cannot rely on its
/// own mouse events.
/// </summary>
public static unsafe class Pointer
{
    static readonly string[] s_taskbarClasses =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"];

    /// <summary>
    /// Where the pointer is, in physical pixels; null when Windows will not say
    /// (the lock screen or a UAC prompt: another desktop has the input).
    /// </summary>
    public static (int X, int Y)? Position()
    {
        System.Drawing.Point point;
        return PInvoke.GetPhysicalCursorPos(&point) ? (point.X, point.Y) : null;
    }

    /// <summary>The key or mouse button is held right now.</summary>
    internal static bool KeyDown(VIRTUAL_KEY key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;

    /// <summary>The taskbar (on any monitor) or the notification area's overflow is under the point.</summary>
    public static bool OnTaskbar(int x, int y)
    {
        var hwnd = PInvoke.WindowFromPoint(new System.Drawing.Point(x, y));
        if (hwnd.IsNull)
        {
            return false;
        }
        var root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
        Span<char> name = stackalloc char[64];
        int length;
        fixed (char* buffer = name)
        {
            length = PInvoke.GetClassName(root, buffer, name.Length);
        }
        return length > 0 && s_taskbarClasses.Contains(name[..length].ToString());
    }
}
