using Tack.Windows.Host;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Shell;

/// <summary>
/// Tack's icon in the notification area. It adds itself again when Explorer
/// restarts, reports clicks through its host window, and swaps its glyph when
/// the taskbar's theme or the screen's scale changes. Windows tells the host
/// window of both, so nothing waits on the registry as Tack 0.2's thread did.
/// </summary>
public sealed unsafe class TrayIcon : IDisposable
{
    const uint CallbackMessage = PInvoke.WM_APP + 1;
    const int TooltipLength = 127;

    readonly HostWindow _host;
    readonly string _tooltip;
    readonly TrayGlyph _glyph;

    /// <param name="glyphFolder">Where the tray-light-*.png and tray-dark-*.png glyphs are.</param>
    /// <param name="fallbackIcon">The app's .ico, shown if a glyph will not load.</param>
    public TrayIcon(HostWindow host, string tooltip, string glyphFolder, string fallbackIcon)
    {
        (_host, _tooltip) = (host, tooltip);
        _glyph = new TrayGlyph(glyphFolder, fallbackIcon);
        host.Listen(CallbackMessage, (_, lParam) => OnMouse((uint)lParam));
        host.Listen(PInvoke.RegisterWindowMessage("TaskbarCreated"), (_, _) => OnTaskbarCreated());
        // Any setting change, not only "ImmersiveColorSet" (light or dark): a hidden window is not
        // sure to hear WM_DPICHANGED, and the glyph is swapped only when the right one differs.
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

    /// <summary>Explorer started again, perhaps in another theme: the icon goes back with the glyph for now.</summary>
    void OnTaskbarCreated()
    {
        _glyph.Refresh();
        Add();
    }

    /// <summary>Shows the glyph for the theme and scale as they are now, if that is another one.</summary>
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
