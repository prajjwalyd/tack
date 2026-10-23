using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Tack.Core.Diagnostics;
using Tack.Core.Geometry;
using Tack.Windows.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Edge;

/// <summary>
/// The edge strips' thread: its message loop, the strip windows, and what the
/// pointer does on them (<see cref="StripPointer"/>). Everything here runs on
/// that one thread.
/// </summary>
internal sealed unsafe class StripThread
{
    public const string ClassName = "TackEdgeStrip";

    /// <summary>From <see cref="EdgeStrips"/>: wParam 1 makes the strips, 0 takes them down.</summary>
    public static readonly uint EnableMessage = PInvoke.RegisterWindowMessage("Tack.EdgeStrips.Enable");

    /// <summary>The monitors changed: the zones move.</summary>
    static readonly uint s_relayoutMessage = PInvoke.RegisterWindowMessage("Tack.EdgeStrips.Relayout");

    [ThreadStatic]
    static StripThread? t_current;

    readonly Action<EdgeEvent> _onEvent;
    readonly HINSTANCE _instance;
    readonly List<(HWND Window, Rect Zone)> _strips = [];

    /// <summary>Every strip's drop target, as the COM object OLE is given.</summary>
    readonly void* _dropTarget;
    bool _enabled;

    StripThread(Action<EdgeEvent> onEvent, HINSTANCE instance)
    {
        (_onEvent, _instance) = (onEvent, instance);
        Pointer = new StripPointer(Emit);
        _dropTarget = ComInterfaceMarshaller<IOleDropTarget>.ConvertToUnmanaged(new StripDropTarget(Emit));
    }

    /// <summary>The strips' state on this thread; null on any other.</summary>
    public static StripThread? Current => t_current;

    public StripPointer Pointer { get; }

    /// <summary>The thread's body: sets up, reports its id through <paramref name="started"/>, and loops until WM_QUIT.</summary>
    public static void Run(Action<EdgeEvent> onEvent, TaskCompletionSource<uint> started)
    {
        HINSTANCE instance;
        try
        {
            instance = Setup();
        }
        catch (ExternalException e)
        {
            started.SetException(e);
            return;
        }
        var self = new StripThread(onEvent, instance);
        t_current = self;
        MSG msg;
        // Makes the thread's queue, so it can be posted to.
        PInvoke.PeekMessage(&msg, HWND.Null, PInvoke.WM_USER, PInvoke.WM_USER, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        started.SetResult(PInvoke.GetCurrentThreadId());
        try
        {
            self.Loop();
        }
        finally
        {
            self._enabled = false;
            self.Relayout();
            ComInterfaceMarshaller<IOleDropTarget>.Free(self._dropTarget);
            t_current = null;
            fixed (char* name = ClassName)
            {
                PInvoke.UnregisterClass(name, instance);
            }
            PInvoke.OleUninitialize();
        }
    }

    void Loop()
    {
        MSG msg;
        while (PInvoke.GetMessage(&msg, HWND.Null, 0, 0).Value > 0)
        {
            if (!msg.hwnd.IsNull)
            {
                PInvoke.DispatchMessage(&msg);
                continue;
            }
            if (msg.message == EnableMessage)
            {
                _enabled = msg.wParam.Value != 0;
            }
            else if (msg.message != s_relayoutMessage)
            {
                continue;
            }
            Relayout();
        }
    }

    static HINSTANCE Setup()
    {
        Marshal.ThrowExceptionForHR(PInvoke.OleInitialize(null).Value);
        HINSTANCE instance = PInvoke.GetModuleHandle((PCWSTR)null);
        fixed (char* name = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                hbrBackground = (HBRUSH)(nint)PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.BLACK_BRUSH),
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(&windowClass) == 0)
            {
                var error = new Win32Exception();
                PInvoke.OleUninitialize();
                throw error;
            }
        }
        return instance;
    }

    /// <summary>
    /// Makes the strips match the monitors (none when not enabled); leaves them
    /// alone if they already do. A monitor added or removed, a resolution or DPI
    /// change: the zones move.
    /// </summary>
    void Relayout()
    {
        var zones = _enabled
            ? Monitors.All().Select(m => EdgeZone.Of(m, EdgeZone.Thickness)).OrderBy(z => z.X).ThenBy(z => z.Y).ToList()
            : [];
        if (_strips.Select(s => s.Zone).SequenceEqual(zones))
        {
            return;
        }
        foreach (var (window, _) in _strips)
        {
            PInvoke.RevokeDragDrop(window);
            PInvoke.DestroyWindow(window);
        }
        _strips.Clear();
        // Gone with its window, so it is not on a strip any more.
        Pointer.Forget();
        foreach (var zone in zones)
        {
            try
            {
                _strips.Add((Create(zone), zone));
            }
            catch (ExternalException e)
            {
                DevTrace.Write($"cannot make an edge strip at {zone}: {e.Message}");
            }
        }
    }

    HWND Create(Rect zone)
    {
        // Never activated, and not in Alt+Tab or the taskbar. Layered with a
        // constant alpha of 1: all but invisible, yet still hit-tested.
        const WINDOW_EX_STYLE Style = WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
            | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_LAYERED;
        HWND hwnd;
        fixed (char* name = ClassName)
        {
            hwnd = PInvoke.CreateWindowEx(
                Style, name, null, WINDOW_STYLE.WS_POPUP, zone.X, zone.Y, zone.W, zone.H,
                HWND.Null, HMENU.Null, _instance, null);
        }
        if (hwnd.IsNull)
        {
            throw new Win32Exception();
        }
        try
        {
            if (!PInvoke.SetLayeredWindowAttributes(hwnd, default, 1, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA))
            {
                throw new Win32Exception();
            }
            Marshal.ThrowExceptionForHR(PInvoke.RegisterDragDrop(hwnd, (IDropTarget*)_dropTarget).Value);
            // Again, in physical px: a window made on a monitor of another DPI
            // may not come out where it was asked.
            if (!PInvoke.SetWindowPos(
                hwnd, HWND.HWND_TOPMOST, zone.X, zone.Y, zone.W, zone.H,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW))
            {
                throw new Win32Exception();
            }
        }
        catch (ExternalException)
        {
            PInvoke.RevokeDragDrop(hwnd);
            PInvoke.DestroyWindow(hwnd);
            throw;
        }
        return hwnd;
    }

    /// <summary>Tells the poller; an exception must never unwind into Windows.</summary>
    void Emit(EdgeEvent edgeEvent)
    {
        try
        {
            _onEvent(edgeEvent);
        }
        catch (Exception e)
        {
            DevTrace.Write($"edge strips: telling {edgeEvent} failed: {e}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    static LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEACTIVATE:
                return (LRESULT)(nint)PInvoke.MA_NOACTIVATE;
            case PInvoke.WM_MOUSEMOVE:
                t_current?.Pointer.Arrive(hwnd);
                break;
            case PInvoke.WM_TIMER when wParam.Value == StripPointer.LeaveTimer:
                t_current?.Pointer.CheckLeave(hwnd);
                break;
            case PInvoke.WM_DISPLAYCHANGE or PInvoke.WM_DPICHANGED:
                // Not rebuilt from inside a strip's own message.
                PInvoke.PostThreadMessage(PInvoke.GetCurrentThreadId(), s_relayoutMessage, 0, 0);
                return default;
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
