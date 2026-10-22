using Tack.Core.Diagnostics;
using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Windows.Host;
using Tack.Windows.Hotkeys;

namespace Tack.App;

/// <summary>
/// Tack's two global shortcuts, show-or-hide and pin the selection: registered
/// from the settings at startup, and what a press does. Presses arrive on the
/// UI thread, through the host window.
/// </summary>
sealed class Shortcuts : IDisposable
{
    readonly GlobalHotkeys _hotkeys;
    readonly Reveal _reveal;
    readonly Pinning _pinning;

    /// <summary>Registers the saved shortcuts; on the UI thread, which owns <paramref name="host"/>.</summary>
    public Shortcuts(HostWindow host, TackSettings settings, Reveal reveal, Pinning pinning)
    {
        (_reveal, _pinning) = (reveal, pinning);
        _hotkeys = new GlobalHotkeys(host);
        _hotkeys.Pressed += OnPressed;
        Statuses = _hotkeys.Set(settings.ToggleChord(), settings.PinChord());
        if (Statuses.AnyInUse)
        {
            DevTrace.Write($"shortcuts: another app has one of them (show or hide: {Statuses.Toggle}, pin: {Statuses.Pin})");
        }
    }

    /// <summary>How registering them went; a shortcut another app has is <see cref="HotkeyStatus.InUse"/>.</summary>
    public HotkeyStatuses Statuses { get; }

    void OnPressed(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.Toggle:
                _reveal.Toggle(RevealReason.Hotkey);
                break;
            case HotkeyAction.PinSelection:
                _pinning.OnShortcut();
                break;
        }
    }

    public void Dispose() => _hotkeys.Dispose();
}
