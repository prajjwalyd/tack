using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Tests.Selection;

/// <summary>
/// A hidden Win32 edit box with some of its text selected, on a thread of its
/// own that answers messages, as an app's window would; UI Automation reads it
/// through Windows' own provider for edit boxes.
/// </summary>
sealed unsafe partial class EditBox : IDisposable
{
    const uint PasswordStyle = 0x20;
    const uint SetSelection = 0xB1;

    readonly Thread _thread;
    uint _threadId;

    public EditBox(string text, int start, int end, bool password = false)
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = PInvoke.GetCurrentThreadId();
            fixed (char* edit = "EDIT", content = text)
            {
                var style = (WINDOW_STYLE)(0x80000000 /* WS_POPUP */ | (password ? PasswordStyle : 0));
                var hwnd = PInvoke.CreateWindowEx(0, edit, content, style, 0, 0, 200, 50, HWND.Null, HMENU.Null, HINSTANCE.Null, null);
                Handle = (nint)hwnd.Value;
                SendMessage(Handle, SetSelection, start, end);
            }
            ready.Set();
            MSG msg;
            while (PInvoke.GetMessage(&msg, HWND.Null, 0, 0) > 0)
            {
                PInvoke.DispatchMessage(&msg);
            }
            PInvoke.DestroyWindow((HWND)Handle);
        })
        { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    public nint Handle { get; private set; }

    public void Dispose()
    {
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join();
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
