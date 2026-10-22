using Tack.Core;
using Tack.Core.Reveal;
using Tack.Windows.Host;
using Tack.Windows.Screenshots;
using Tack.Windows.Shell;
using Tack.Windows.Startup;

namespace Tack.App;

/// <summary>
/// The notification area icon and its menu. A left click shows or hides the
/// board. Without a board yet, the menu holds only what works without one.
/// </summary>
sealed class Tray : IDisposable
{
    readonly HostWindow _host;
    readonly Autostart _autostart;
    readonly Action _quit;
    readonly TrayIcon _icon;

    public Tray(HostWindow host, AppIdentity identity, Autostart autostart, Reveal reveal, Action quit)
    {
        (_host, _autostart, _quit) = (host, autostart, quit);
        _icon = new TrayIcon(host, identity.DisplayName, Path.Combine(AppContext.BaseDirectory, "Assets", "Tack.ico"));
        _icon.Clicked += () => reveal.Toggle(RevealReason.Tray);
        _icon.MenuRequested += ShowMenu;
    }

    void ShowMenu() => PopupMenu.ShowAtPointer(_host.Handle,
    [
        new MenuItem("Open Screenshots folder", () => ShellLauncher.Open(ScreenshotsFolder.Find())),
        MenuItem.Separator,
        // Read from the Run key each time, not from board.json.
        new MenuItem("Start with Windows", () => _autostart.Set(!_autostart.Enabled), Checked: _autostart.Enabled),
        MenuItem.Separator,
        new MenuItem("Quit Tack", _quit),
    ]);

    public void Dispose() => _icon.Dispose();
}
