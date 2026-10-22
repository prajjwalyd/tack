using Windows.Win32;
using Windows.Win32.Foundation;

namespace Tack.Windows.Windowing;

/// <summary>The scale a window is drawn at: its monitor's DPI over 96, as Windows has it for that window.</summary>
public static class WindowScale
{
    /// <summary>The window's DPI over 96; 1 if Windows cannot say.</summary>
    public static double Of(nint hwnd)
    {
        var dpi = PInvoke.GetDpiForWindow((HWND)hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }
}
