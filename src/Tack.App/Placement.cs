using Tack.Windows.Display;
using Tack.Windows.Input;

namespace Tack.App;

/// <summary>
/// Where the board's window goes on a monitor: its whole work area. Being that
/// tall lets a capture fly in from anywhere without a resize, which can flash,
/// and stopping short of the taskbar keeps Windows from taking it for a
/// full-screen app. Physical px.
/// </summary>
/// <param name="MonitorTop">The top of the monitor, not of its work area.</param>
/// <param name="Scale">The monitor's DPI over 96.</param>
sealed record Placement((int X, int Y) Position, (int W, int H) Size, int MonitorTop, double Scale)
{
    /// <summary>On the monitor holding the point, or the nearest one.</summary>
    public static Placement At(int x, int y)
    {
        var monitor = Monitors.At(x, y);
        var work = monitor.Work;
        return new Placement((work.X, work.Y), (work.W, work.H), monitor.Bounds.Y, monitor.Scale);
    }

    /// <summary>On the monitor under the pointer.</summary>
    public static Placement UnderPointer() => Pointer.Position() is var (x, y) ? At(x, y) : At(0, 0);
}
