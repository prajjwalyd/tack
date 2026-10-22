using Tack.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Tack.Windows.Capture;

/// <summary>
/// Copies part of the screen into memory with GDI, for as long as a callback
/// runs, then frees it. Coordinates are physical only on a thread that is
/// per-monitor DPI aware (see <see cref="ThreadDpi"/>).
/// </summary>
static unsafe class ScreenCopy
{
    /// <summary>
    /// Copies <paramref name="area"/> of the screen and hands it to <paramref name="use"/>;
    /// default if the area is empty or the screen could not be read (the lock
    /// screen, a secure desktop).
    /// </summary>
    public static T? With<T>(Rect area, Func<ScreenPixels, T> use)
    {
        if (area.W <= 0 || area.H <= 0)
        {
            return default;
        }
        var screen = PInvoke.GetDC(HWND.Null);
        if (screen.IsNull)
        {
            return default;
        }
        var memory = PInvoke.CreateCompatibleDC(screen);
        try
        {
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = area.W,
                    // Negative: top-down rows.
                    biHeight = -area.H,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = (uint)BI_COMPRESSION.BI_RGB,
                },
            };
            void* bits;
            var dib = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (dib.IsNull || bits == null)
            {
                return default;
            }
            var previous = PInvoke.SelectObject(memory, (HGDIOBJ)dib.Value);
            try
            {
                var copied = PInvoke.BitBlt(memory, 0, 0, area.W, area.H, screen, area.X, area.Y, ROP_CODE.SRCCOPY);
                return copied ? use(new ScreenPixels(area, (byte*)bits)) : default;
            }
            finally
            {
                PInvoke.SelectObject(memory, previous);
                PInvoke.DeleteObject((HGDIOBJ)dib.Value);
            }
        }
        finally
        {
            PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }
}
