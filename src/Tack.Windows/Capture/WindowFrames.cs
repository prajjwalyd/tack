using Tack.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Capture;

/// <summary>Windows on screen, by their visible frames.</summary>
static unsafe class WindowFrames
{
    /// <summary>
    /// The visible frame of the top-level window under <paramref name="point"/>
    /// (physical px, on a per-monitor aware thread); its shadow is left out,
    /// as a window snip leaves it out.
    /// </summary>
    public static Rect? Under((int X, int Y) point)
    {
        var hwnd = PInvoke.WindowFromPhysicalPoint(new System.Drawing.Point(point.X, point.Y));
        if (hwnd.IsNull)
        {
            return null;
        }
        var root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root.IsNull)
        {
            root = hwnd;
        }
        RECT r;
        if (PInvoke.DwmGetWindowAttribute(root, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT)).Failed)
        {
            return null;
        }
        return new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top);
    }
}
