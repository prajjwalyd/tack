using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Windowing;

/// <summary>
/// Asks for the person's attention without taking the focus from anything:
/// the window's taskbar button flashes until the window comes to the front,
/// as Windows does for a request that is informational, not critical.
/// </summary>
public static unsafe class Attention
{
    public static void Ask(nint hwnd)
    {
        var flash = new FLASHWINFO
        {
            cbSize = (uint)sizeof(FLASHWINFO),
            hwnd = (HWND)hwnd,
            dwFlags = FLASHWINFO_FLAGS.FLASHW_TRAY | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
        };
        PInvoke.FlashWindowEx(in flash);
    }
}
