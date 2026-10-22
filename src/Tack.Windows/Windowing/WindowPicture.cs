using Tack.Windows.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace Tack.Windows.Windowing;

/// <summary>
/// A picture of one of Tack's own windows as Windows composes it, title bar
/// and all, for development tools. The window draws itself into memory, so
/// nothing else on screen (another window over it, the desktop) can end up in
/// the picture; the invisible resize borders round it are left out.
/// </summary>
public static unsafe class WindowPicture
{
    /// <summary>Also draws content the UI framework composes with DirectX, which a plain print leaves black.</summary>
    const PRINT_WINDOW_FLAGS RenderFullContent = (PRINT_WINDOW_FLAGS)2;

    /// <summary>The window as a picture, or null if Windows cannot draw it.</summary>
    public static Picture? Of(nint hwnd)
    {
        var window = (HWND)hwnd;
        if (!PInvoke.GetWindowRect(window, out var outer))
        {
            return null;
        }
        RECT seen;
        if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &seen, (uint)sizeof(RECT)).Failed)
        {
            seen = outer;
        }
        var (w, h) = (outer.right - outer.left, outer.bottom - outer.top);
        var (left, top) = (seen.left - outer.left, seen.top - outer.top);
        var (seenW, seenH) = (seen.right - seen.left, seen.bottom - seen.top);
        if (w <= 0 || h <= 0 || seenW <= 0 || seenH <= 0)
        {
            return null;
        }
        var screen = PInvoke.GetDC(HWND.Null);
        var memory = PInvoke.CreateCompatibleDC(screen);
        try
        {
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = w,
                    // Negative: top-down rows.
                    biHeight = -h,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = (uint)BI_COMPRESSION.BI_RGB,
                },
            };
            void* bits;
            var dib = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (dib.IsNull || bits == null)
            {
                return null;
            }
            var previous = PInvoke.SelectObject(memory, (HGDIOBJ)dib.Value);
            try
            {
                return PInvoke.PrintWindow(window, memory, RenderFullContent)
                    ? Crop((byte*)bits, w, left, top, seenW, seenH)
                    : null;
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

    /// <summary>The part of the window that shows, opaque.</summary>
    static Picture Crop(byte* bits, int stridePixels, int left, int top, int w, int h)
    {
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            new ReadOnlySpan<byte>(bits + (((top + y) * stridePixels) + left) * 4, w * 4).CopyTo(bgra.AsSpan(y * w * 4));
        }
        for (var at = 3; at < bgra.Length; at += 4)
        {
            bgra[at] = 255;
        }
        return new Picture(w, h, bgra);
    }
}
