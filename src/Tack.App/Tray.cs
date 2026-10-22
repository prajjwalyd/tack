using Tack.Core;
using Tack.Core.Reveal;
using Tack.Windows.Host;
using Tack.Windows.Screenshots;
using Tack.Windows.Shell;
using Tack.Windows.Startup;

namespace Tack.App;

/// <summary>
/// The notification area icon and its menu. A left click shows or hides the
/// board. The menu holds what works so far: Settings, the phone and updates
/// come later.
/// </summary>
sealed class Tray : IDisposable
{
    readonly HostWindow _host;
    readonly Autostart _autostart;
    readonly Reveal _reveal;
    readonly Unpinning _unpinning;
    readonly SettingsChanges _settings;
    readonly Action _quit;
    readonly TrayIcon _icon;

    public Tray(HostWindow host, AppIdentity identity, Autostart autostart, Reveal reveal, Unpinning unpinning, SettingsChanges settings, Action quit)
    {
        (_host, _autostart, _reveal, _unpinning, _settings, _quit) = (host, autostart, reveal, unpinning, settings, quit);
        _icon = new TrayIcon(host, identity.DisplayName, Path.Combine(AppContext.BaseDirectory, "Assets", "Tack.ico"));
        _icon.Clicked += () => reveal.Toggle(RevealReason.Tray);
        _icon.MenuRequested += ShowMenu;
    }

    void ShowMenu()
    {
        var settings = _settings.Current;
        // The shortcut sits in the menu's accelerator column, as text: Tack registers it itself.
        var show = settings.ToggleChord() is { } chord ? $"Show board\t{chord}" : "Show board";
        PopupMenu.ShowAtPointer(_host.Handle,
        [
            new MenuItem(show, () => _reveal.Show(RevealReason.Tray)),
            MenuItem.Separator,
            new MenuItem("Clear board", _unpinning.Clear),
            new MenuItem("Open Screenshots folder", () => ShellLauncher.Open(ScreenshotsFolder.Find())),
            MenuItem.Separator,
            new MenuItem("Reveal at top edge", () => _settings.SetEdgeReveal(!settings.EdgeReveal), Checked: settings.EdgeReveal),
            new MenuItem("Sound", () => _settings.SetSound(!settings.Sound), Checked: settings.Sound),
            // Read from the Run key each time, not from board.json.
            new MenuItem("Start with Windows", () => _autostart.Set(!_autostart.Enabled), Checked: _autostart.Enabled),
            MenuItem.Separator,
            new MenuItem("Quit Tack", _quit),
        ]);
    }

    public void Dispose() => _icon.Dispose();
}
