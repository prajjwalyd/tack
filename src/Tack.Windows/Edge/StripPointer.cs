using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Edge;

/// <summary>
/// The pointer on a strip, on the strips' thread.
/// <list type="bullet">
/// <item>It arrives: the first mouse move on a strip. The strip turns
/// click-through, so a click on the top pixel row goes to the app below, and a
/// 25 ms timer, which runs only from then, watches for the pointer leaving.</item>
/// <item>A button down on arrival may be a drag on its way: the strip then
/// stays hit-testable, or no drag could enter it, until the button is up.</item>
/// </list>
/// </summary>
internal sealed unsafe class StripPointer(Action<EdgeEvent> emit)
{
    public const nuint LeaveTimer = 1;

    /// <summary>How often an arrived pointer is looked at, to see it leave, ms.</summary>
    const uint LeaveCheckMs = 25;

    /// <summary>The strip the pointer is on, and whether a button was down on arrival.</summary>
    (HWND Strip, bool Held)? _arrived;

    /// <summary>The first mouse move on a strip.</summary>
    public void Arrive(HWND strip)
    {
        if (_arrived != null)
        {
            return;
        }
        var held = Input.Pointer.KeyDown(VIRTUAL_KEY.VK_LBUTTON);
        _arrived = (strip, held);
        if (!held)
        {
            SetClickThrough(strip, true);
        }
        PInvoke.SetTimer(strip, LeaveTimer, LeaveCheckMs, null);
        emit(EdgeEvent.Arrived);
    }

    /// <summary>
    /// The timer's look at the pointer: gone from the strip, or the held button
    /// let go (no drag now, so clicks pass through).
    /// </summary>
    public void CheckLeave(HWND strip)
    {
        if (_arrived is not { } arrived || arrived.Strip != strip)
        {
            return;
        }
        if (IsInside(strip))
        {
            if (arrived.Held && !Input.Pointer.KeyDown(VIRTUAL_KEY.VK_LBUTTON))
            {
                _arrived = (strip, false);
                SetClickThrough(strip, true);
            }
            return;
        }
        PInvoke.KillTimer(strip, LeaveTimer);
        SetClickThrough(strip, false);
        _arrived = null;
        emit(EdgeEvent.Left);
    }

    /// <summary>The strips were taken down or remade: the pointer is on none of them now.</summary>
    public void Forget()
    {
        if (_arrived != null)
        {
            _arrived = null;
            emit(EdgeEvent.Left);
        }
    }

    /// <summary>The pointer is on the strip. Both are read in the thread's own coordinates, so they agree whatever its DPI awareness.</summary>
    static bool IsInside(HWND strip)
    {
        RECT rect;
        System.Drawing.Point point;
        return PInvoke.GetWindowRect(strip, &rect) && PInvoke.GetCursorPos(&point)
            && point.X >= rect.left && point.X < rect.right && point.Y >= rect.top && point.Y < rect.bottom;
    }

    /// <summary>Mouse events pass through the strip, to the window below, or not.</summary>
    static void SetClickThrough(HWND strip, bool on) => ExStyle.Set(strip, WINDOW_EX_STYLE.WS_EX_TRANSPARENT, on);
}
