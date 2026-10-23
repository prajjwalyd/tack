using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tack.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Host;

/// <summary>
/// Tack's hidden window: where Windows sends Tack's own messages, such as the
/// tray icon's clicks. It lives on the UI thread, whose message loop the UI
/// framework already runs, and sleeps until a message comes: nothing polls.
/// </summary>
public sealed unsafe class HostWindow : IDisposable
{
    public const string ClassName = "TackHost";

    static HostWindow? s_current;

    readonly Dictionary<uint, Func<nuint, nint, nint>> _listeners = [];

    public HostWindow()
    {
        if (s_current != null)
        {
            throw new InvalidOperationException("Tack has one host window.");
        }
        HINSTANCE instance = PInvoke.GetModuleHandle((PCWSTR)null);
        fixed (char* name = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(&windowClass) == 0)
            {
                throw new Win32Exception();
            }
            // Hidden and top-level rather than message-only: only top-level
            // windows hear broadcasts such as Explorer's TaskbarCreated.
            var hwnd = PInvoke.CreateWindowEx(0, name, name, 0, 0, 0, 0, 0, HWND.Null, HMENU.Null, instance, null);
            if (hwnd.IsNull)
            {
                throw new Win32Exception();
            }
            Handle = (nint)hwnd.Value;
        }
        s_current = this;
    }

    public nint Handle { get; }

    /// <summary>Calls <paramref name="handler"/> with wParam and lParam whenever <paramref name="message"/> arrives.</summary>
    public void Listen(uint message, Action<nuint, nint> handler) => _listeners[message] = (wParam, lParam) =>
    {
        handler(wParam, lParam);
        return 0;
    };

    /// <summary>
    /// As <see cref="Listen"/>, for a message whose answer matters to Windows:
    /// <paramref name="handler"/>'s result is returned for it. Should the
    /// handler fail, Windows is answered 0.
    /// </summary>
    public void Answer(uint message, Func<nuint, nint, nint> handler) => _listeners[message] = handler;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (s_current != null && s_current._listeners.TryGetValue(message, out var handler))
        {
            // An exception must never unwind into Windows: it would end the process.
            try
            {
                return new LRESULT(handler(wParam.Value, lParam.Value));
            }
            catch (Exception e)
            {
                DevTrace.Write($"host window: message 0x{message:x} failed: {e}");
                return default;
            }
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (s_current != this)
        {
            return;
        }
        PInvoke.DestroyWindow((HWND)Handle);
        // So a later host window in the same process can register the class again.
        HINSTANCE instance = PInvoke.GetModuleHandle((PCWSTR)null);
        fixed (char* name = ClassName)
        {
            PInvoke.UnregisterClass(name, instance);
        }
        s_current = null;
    }
}
