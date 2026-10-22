using Tack.Core.Diagnostics;
using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Core.Shortcuts;
using Tack.Windows.Host;
using Tack.Windows.Hotkeys;

namespace Tack.App;

/// <summary>
/// Tack's two global shortcuts, show-or-hide and pin the selection: registered
/// from the settings at startup, changed from Settings, paused while Settings
/// listens for a new chord, and what a press does. Presses arrive on the UI
/// thread, through the host window; every member is for the UI thread too.
/// </summary>
sealed class Shortcuts : IDisposable
{
    readonly GlobalHotkeys _hotkeys;
    readonly Reveal _reveal;
    readonly Pinning _pinning;
    bool _paused;

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

    /// <summary>How the last registration went; a shortcut another app has is <see cref="HotkeyStatus.InUse"/>.</summary>
    public HotkeyStatuses Statuses { get; private set; }

    /// <summary>The saved shortcuts of <paramref name="settings"/> and how registering them went.</summary>
    public ShortcutPair Current(TackSettings settings) => new(
        new ShortcutState(settings.ToggleChord()?.ToString() ?? "", StatusOf(Statuses.Toggle)),
        new ShortcutState(settings.PinChord()?.ToString() ?? "", StatusOf(Statuses.Pin)));

    /// <summary>The shortcuts another app has, as "Win+Alt+C", for the tray's warning lines.</summary>
    public IEnumerable<string> InUse(TackSettings settings) =>
        new[] { Current(settings).Toggle, Current(settings).Pin }
            .Where(s => s.Status == ShortcutStatus.InUse && s.Chord.Length > 0)
            .Select(s => s.Chord);

    /// <summary>
    /// Registers <paramref name="plan"/>'s chords (it must be one that
    /// <see cref="ShortcutPlan.CanTry"/>). If either is taken, the ones of
    /// <paramref name="before"/> are registered again. Says how each fared; the
    /// caller saves the pair only if <see cref="ShortcutPair.AllWork"/>.
    /// </summary>
    public ShortcutPair TrySet(ShortcutPlan plan, TackSettings before)
    {
        // While paused a chord is only remembered, so it is tried for real first.
        if (_paused)
        {
            _hotkeys.Pause(false);
        }
        var statuses = _hotkeys.Set(plan.Toggle, plan.Pin);
        Statuses = statuses.AnyInUse ? _hotkeys.Set(before.ToggleChord(), before.PinChord()) : statuses;
        if (_paused)
        {
            _hotkeys.Pause(true);
        }
        return new ShortcutPair(
            plan.Answer.Toggle with { Status = StatusOf(statuses.Toggle) },
            plan.Answer.Pin with { Status = StatusOf(statuses.Pin) });
    }

    /// <summary>
    /// Lets go of both shortcuts while Settings listens for a chord, so pressing
    /// Tack's own reaches it, and takes them back after.
    /// </summary>
    public void Pause(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }
        _paused = paused;
        var statuses = _hotkeys.Pause(paused);
        if (!paused)
        {
            Statuses = statuses;
        }
    }

    static ShortcutStatus StatusOf(HotkeyStatus status) => status switch
    {
        HotkeyStatus.Off => ShortcutStatus.Off,
        HotkeyStatus.InUse => ShortcutStatus.InUse,
        _ => ShortcutStatus.Ok,
    };

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
