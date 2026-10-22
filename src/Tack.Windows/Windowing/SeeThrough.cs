using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Windowing;

/// <summary>
/// The Win32 half of a see-through window: the desktop shows wherever the
/// window's content leaves pixels fully transparent (the other half is a
/// transparent backdrop brush). The UI framework has no transparent window,
/// and it takes all of this together, as PowerToys does it: no frame or
/// border, no frame extended, blur-behind on with an empty region, and the
/// background erased to black, which the desktop compositor then draws as
/// transparent. Call it once, on the window's own thread.
/// </summary>
public static unsafe class SeeThrough
{
    /// <summary>Each see-through window's own window procedure, which ours passes messages on to.</summary>
    static readonly Dictionary<nint, nint> s_innerProcs = [];

    public static void Apply(nint hwnd)
    {
        var window = (HWND)hwnd;
        MakeBorderless(window);
        ApplyBlurBehind(window);
        if (s_innerProcs.ContainsKey(hwnd))
        {
            return;
        }
        delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> proc = &WindowProc;
        s_innerProcs[hwnd] = PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWLP_WNDPROC, (nint)proc);
    }

    /// <summary>A plain popup: no caption or frame, and no Windows 11 border or rounded corners.</summary>
    static void MakeBorderless(HWND window)
    {
        var style = (WINDOW_STYLE)PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        style &= ~(WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME | WINDOW_STYLE.WS_SYSMENU
            | WINDOW_STYLE.WS_MINIMIZEBOX | WINDOW_STYLE.WS_MAXIMIZEBOX);
        style |= WINDOW_STYLE.WS_POPUP;
        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (nint)style);
        uint noBorder = PInvoke.DWMWA_COLOR_NONE;
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &noBorder, sizeof(uint));
        var square = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
        PInvoke.DwmSetWindowAttribute(
            window, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &square, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));
        PInvoke.SetWindowPos(
            window, HWND.Null, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    static void ApplyBlurBehind(HWND window)
    {
        var none = new MARGINS();
        PInvoke.DwmExtendFrameIntoClientArea(window, &none);
        var empty = PInvoke.CreateRectRgn(-2, -2, -1, -1);
        var blur = new DWM_BLURBEHIND
        {
            dwFlags = PInvoke.DWM_BB_ENABLE | PInvoke.DWM_BB_BLURREGION,
            fEnable = true,
            hRgnBlur = empty,
        };
        PInvoke.DwmEnableBlurBehindWindow(window, &blur);
        PInvoke.DeleteObject((HGDIOBJ)(nint)empty.Value);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var inner = s_innerProcs[(nint)hwnd.Value];
        switch (message)
        {
            case PInvoke.WM_ERASEBKGND:
                PInvoke.GetClientRect(hwnd, out var client);
                var black = (HBRUSH)(nint)PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.BLACK_BRUSH).Value;
                PInvoke.FillRect((HDC)(nint)wParam.Value, &client, black);
                return (LRESULT)1;
            case PInvoke.WM_DWMCOMPOSITIONCHANGED:
                ApplyBlurBehind(hwnd);
                break;
            case PInvoke.WM_NCDESTROY:
                s_innerProcs.Remove((nint)hwnd.Value);
                break;
        }
        return PInvoke.CallWindowProc(
            (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)inner, hwnd, message, wParam, lParam);
    }
}
