using Tack.Windows.Host;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Shell;

/// <summary>
/// Tack's icon in the notification area. It adds itself again when Explorer
/// restarts, and reports clicks through its host window.
/// </summary>
public sealed unsafe class TrayIcon : IDisposable
{
    const uint CallbackMessage = PInvoke.WM_APP + 1;
    const int TooltipLength = 127;

    readonly HostWindow _host;
    readonly string _tooltip;
    readonly HICON _icon;
    readonly bool _ownsIcon;

    public TrayIcon(HostWindow host, string tooltip, string iconFile)
    {
        (_host, _tooltip) = (host, tooltip);
        (_icon, _ownsIcon) = LoadIcon(iconFile);
        host.Listen(CallbackMessage, (_, lParam) => OnMouse((uint)lParam));
        host.Listen(PInvoke.RegisterWindowMessage("TaskbarCreated"), (_, _) => Add());
        Add();
    }

    /// <summary>The icon was clicked with the left button.</summary>
    public event Action? Clicked;

    /// <summary>The icon was clicked with the right button: time for the menu.</summary>
    public event Action? MenuRequested;

    void OnMouse(uint mouseMessage)
    {
        switch (mouseMessage)
        {
            case PInvoke.WM_LBUTTONUP:
                Clicked?.Invoke();
                break;
            case PInvoke.WM_RBUTTONUP:
                MenuRequested?.Invoke();
                break;
        }
    }

    void Add()
    {
        var data = Identity();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        _tooltip.AsSpan(0, Math.Min(_tooltip.Length, TooltipLength)).CopyTo(data.szTip.AsSpan());
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
    }

    NOTIFYICONDATAW Identity() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = (HWND)_host.Handle,
        uID = 1,
    };

    /// <summary>The icon file at the small-icon size, or Windows' generic icon if it will not load.</summary>
    static (HICON Icon, bool Owned) LoadIcon(string file)
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        fixed (char* path = file)
        {
            var loaded = PInvoke.LoadImage(HINSTANCE.Null, path, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE);
            if (!loaded.IsNull)
            {
                return ((HICON)(nint)loaded.Value, true);
            }
        }
        return (PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION), false);
    }

    public void Dispose()
    {
        var data = Identity();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
        if (_ownsIcon)
        {
            PInvoke.DestroyIcon(_icon);
        }
    }
}
