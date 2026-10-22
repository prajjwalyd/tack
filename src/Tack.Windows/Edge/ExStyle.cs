using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Edge;

/// <summary>A window's extended style bits, turned on or off in place.</summary>
internal static class ExStyle
{
    /// <summary>Sets or clears <paramref name="bits"/>; leaves the window alone when nothing changes.</summary>
    public static void Set(HWND hwnd, WINDOW_EX_STYLE bits, bool on)
    {
        var style = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var updated = on ? style | (nint)bits : style & ~(nint)bits;
        if (updated != style)
        {
            PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, updated);
        }
    }

    public static bool Has(HWND hwnd, WINDOW_EX_STYLE bits) =>
        (PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & (nint)bits) == (nint)bits;
}
