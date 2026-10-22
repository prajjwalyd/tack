using Tack.Core;
using Tack.Core.Reveal;
using Tack.Windows.Host;
using Tack.Windows.Screenshots;
using Tack.Windows.Shell;

namespace Tack.App;

/// <summary>
/// The notification area icon and its menu. A left click shows or hides the
/// board. The menu's switches are settings, changed through
/// <see cref="SettingsChanges"/> as Settings changes them, so the two always
/// agree; the menu is built afresh each time it opens. A shortcut another app
/// has gets a line of its own, which opens Settings at the shortcuts.
/// </summary>
sealed class Tray : IDisposable
{
    readonly HostWindow _host;
    readonly Reveal _reveal;
    readonly Unpinning _unpinning;
    readonly SettingsChanges _settings;
    readonly Action<string?> _openSettings;
    readonly Action _quit;
    readonly TrayIcon _icon;

    /// <param name="openSettings">Opens Settings, at a section ("shortcuts", "phone", "updates") or at its top for null.</param>
    public Tray(
        HostWindow host, AppIdentity identity, Reveal reveal, Unpinning unpinning, SettingsChanges settings,
        Action<string?> openSettings, Action quit)
    {
        (_host, _reveal, _unpinning, _settings, _openSettings, _quit) = (host, reveal, unpinning, settings, openSettings, quit);
        _icon = new TrayIcon(host, identity.DisplayName, Path.Combine(AppContext.BaseDirectory, "Assets", "Tack.ico"));
        _icon.Clicked += () => reveal.Toggle(RevealReason.Tray);
        _icon.MenuRequested += ShowMenu;
    }

    void ShowMenu()
    {
        var settings = _settings.Current;
        var autostart = _settings.Autostart;
        // The shortcut sits in the menu's accelerator column, as text: Tack registers it itself.
        var show = settings.ToggleChord() is { } chord ? $"Show board\t{chord}" : "Show board";
        List<MenuItem> items =
        [
            new MenuItem(show, () => _reveal.Show(RevealReason.Tray)),
            new MenuItem("Use on your phone…", () => _openSettings("phone")),
            MenuItem.Separator,
            new MenuItem("Clear board", _unpinning.Clear),
            new MenuItem("Open Screenshots folder", () => ShellLauncher.Open(ScreenshotsFolder.Find())),
        ];
        // Right under "Open Screenshots folder", so it is seen.
        foreach (var taken in _settings.ShortcutsInUse)
        {
            items.Add(new MenuItem($"{taken} is in use by another app — Change…", () => _openSettings("shortcuts")));
        }
        items.AddRange(
        [
            MenuItem.Separator,
            new MenuItem("Settings…", () => _openSettings(null)),
            new MenuItem("Reveal at top edge", () => _settings.SetEdgeReveal(!settings.EdgeReveal), Checked: settings.EdgeReveal),
            new MenuItem("Sound", () => _settings.SetSound(!settings.Sound), Checked: settings.Sound),
            new MenuItem("Start with Windows", () => _settings.SetAutostart(!autostart), Checked: autostart),
            new MenuItem("Shortcuts…", () => _openSettings("shortcuts")),
            // The updater comes in phase 6; until then this only opens the Updates section.
            new MenuItem("Check for updates…", () => _openSettings("updates")),
            MenuItem.Separator,
            new MenuItem("Quit Tack", _quit),
        ]);
        PopupMenu.ShowAtPointer(_host.Handle, items);
    }

    public void Dispose() => _icon.Dispose();
}
