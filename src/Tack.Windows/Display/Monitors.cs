using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tack.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Display;

/// <summary>The monitors, asked of Windows each time, so a change of displays needs no bookkeeping.</summary>
public static unsafe class Monitors
{
    /// <summary>The monitor holding the point, or the nearest one.</summary>
    public static Monitor At(int x, int y)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = Info(monitor);
        double scale = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Succeeded && dpi > 0
            ? dpi / 96.0
            : 1.0;
        return new Monitor(ToRect(info.rcWork), ToRect(info.rcMonitor), scale);
    }

    /// <summary>Every monitor, whole.</summary>
    public static List<Rect> All()
    {
        var rects = new List<Rect>();
        var handle = GCHandle.Alloc(rects);
        try
        {
            PInvoke.EnumDisplayMonitors(HDC.Null, null, &Collect, (LPARAM)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return rects;
    }

    /// <summary>
    /// A full-screen Direct3D app, presentation mode or a busy state is in
    /// front; Tack then does not reveal on its own.
    /// </summary>
    public static bool FullScreenAppInFront()
    {
        if (PInvoke.SHQueryUserNotificationState(out var state).Failed)
        {
            return false;
        }
        return state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
            or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
            or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static BOOL Collect(HMONITOR monitor, HDC dc, RECT* clip, LPARAM data)
    {
        var rects = (List<Rect>)GCHandle.FromIntPtr(data).Target!;
        rects.Add(ToRect(Info(monitor).rcMonitor));
        return true;
    }

    static MONITORINFO Info(HMONITOR monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, &info);
        return info;
    }

    static Rect ToRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);
}
