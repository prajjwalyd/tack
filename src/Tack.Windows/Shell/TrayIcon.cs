using Tack.Windows.Host;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Shell;

/// <summary>
/// Tack's icon in the notification area. It adds itself again when Explorer
/// restarts, reports clicks through its host window, and loads its icon again
/// at the tray's size when the screen's scale changes.
/// </summary>
public sealed unsafe class TrayIcon : IDisposable
{
    const uint CallbackMessage = PInvoke.WM_APP + 1;
    const int TooltipLength = 127;

    readonly HostWindow _host;
    readonly string _tooltip;
    readonly TrayGlyph _glyph;

    /// <param name="ico">The app's .ico.</param>
    public TrayIcon(HostWindow host, string tooltip, string ico)
    {
        (_host, _tooltip) = (host, tooltip);
        _glyph = new TrayGlyph(ico);
        host.Listen(CallbackMessage, (_, lParam) => OnMouse((uint)lParam));
        host.Listen(PInvoke.RegisterWindowMessage("TaskbarCreated"), (_, _) => OnTaskbarCreated());
        // Any setting change too: a hidden window is not sure to hear WM_DPICHANGED,
        // and the icon is loaded again only when its size differs.
        host.Listen(PInvoke.WM_SETTINGCHANGE, (_, _) => UpdateGlyph());
        host.Listen(PInvoke.WM_DPICHANGED, (_, _) => UpdateGlyph());
        host.Listen(PInvoke.WM_DISPLAYCHANGE, (_, _) => UpdateGlyph());
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

    /// <summary>Explorer started again: the icon goes back, at the size for now.</summary>
    void OnTaskbarCreated()
    {
        _glyph.Refresh();
        Add();
    }

    /// <summary>Shows the icon at the tray's size for the scale as it is now, if that is another.</summary>
    void UpdateGlyph()
    {
        if (!_glyph.Refresh())
        {
            return;
        }
        var data = Identity();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON;
        data.hIcon = _glyph.Icon;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
    }

    void Add()
    {
        var data = Identity();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _glyph.Icon;
        _tooltip.AsSpan(0, Math.Min(_tooltip.Length, TooltipLength)).CopyTo(data.szTip.AsSpan());
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
    }

    NOTIFYICONDATAW Identity() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = (HWND)_host.Handle,
        uID = 1,
    };

    public void Dispose()
    {
        var data = Identity();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
        _glyph.Dispose();
    }
}
