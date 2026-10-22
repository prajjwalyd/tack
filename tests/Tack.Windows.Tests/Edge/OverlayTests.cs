using System.ComponentModel;
using Tack.Windows.Edge;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Tests.Edge;

public sealed unsafe class OverlayTests
{
    const WINDOW_EX_STYLE ClickThrough = WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_LAYERED;
    const WINDOW_EX_STYLE Unfocusable = WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;

    /// <summary>A plain window that is never shown.</summary>
    static HWND Hidden()
    {
        fixed (char* className = "STATIC")
        {
            var hwnd = PInvoke.CreateWindowEx(
                0, className, null, WINDOW_STYLE.WS_POPUP, 0, 0, 10, 10, HWND.Null, HMENU.Null, HINSTANCE.Null, null);
            return hwnd.IsNull ? throw new Win32Exception() : hwnd;
        }
    }

    [Fact]
    public void Click_through_comes_and_goes_and_the_window_stays_unfocusable()
    {
        var hwnd = Hidden();
        try
        {
            Overlay.MakeUnfocusable(hwnd);
            Overlay.SetClickThrough(hwnd, true);
            Assert.True(ExStyle.Has(hwnd, ClickThrough) && ExStyle.Has(hwnd, Unfocusable));
            Overlay.SetClickThrough(hwnd, false);
            Assert.False(ExStyle.Has(hwnd, WINDOW_EX_STYLE.WS_EX_TRANSPARENT));
            Assert.False(ExStyle.Has(hwnd, WINDOW_EX_STYLE.WS_EX_LAYERED));
            Assert.True(ExStyle.Has(hwnd, Unfocusable));
        }
        finally
        {
            PInvoke.DestroyWindow(hwnd);
        }
    }
}
