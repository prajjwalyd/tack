using System.Runtime.InteropServices;
using Tack.Core.Diagnostics;
using Tack.Core.Shortcuts;
using Tack.Windows.Host;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Tack.Windows.Hotkeys;

/// <summary>
/// Tack's global shortcuts: show or hide the board, and pin the selection.
/// Either can be changed, turned off, or paused while Settings listens for a
/// new chord. They are registered to the host window, so Windows reports a
/// press as a message on the UI thread: no thread of their own.
/// </summary>
/// <remarks>
/// Windows binds a shortcut to the thread that owns its window, so every
/// member must be called on the UI thread, the one that made the host window.
/// </remarks>
public sealed class GlobalHotkeys : IDisposable
{
    readonly HostWindow _host;
    Chord? _toggle;
    Chord? _pin;
    bool _paused;

    /// <summary>Starts with no shortcuts; <see cref="Set"/> registers them.</summary>
    public GlobalHotkeys(HostWindow host)
    {
        _host = host;
        host.Listen(PInvoke.WM_HOTKEY, (wParam, _) => OnHotkey(wParam));
    }

    /// <summary>A shortcut was pressed. Raised on the UI thread.</summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>
    /// Registers <paramref name="toggle"/> and <paramref name="pin"/> (null: off)
    /// in place of whatever was registered, and says how each went. While paused
    /// they are only remembered, and registered when the pause ends.
    /// </summary>
    public HotkeyStatuses Set(Chord? toggle, Chord? pin)
    {
        (_toggle, _pin) = (toggle, pin);
        return _paused ? WhilePaused(toggle, pin) : Register();
    }

    /// <summary>
    /// Lets go of the shortcuts while <paramref name="paused"/>, so Settings can
    /// hear any chord, Tack's own included, and registers them again after.
    /// </summary>
    public HotkeyStatuses Pause(bool paused)
    {
        _paused = paused;
        if (!paused)
        {
            return Register();
        }
        UnregisterAll();
        return WhilePaused(_toggle, _pin);
    }

    internal void OnHotkey(nuint id)
    {
        if (id is (nuint)HotkeyAction.Toggle or (nuint)HotkeyAction.PinSelection)
        {
            Pressed?.Invoke((HotkeyAction)id);
        }
    }

    /// <summary>While paused nothing is registered; a chord counts as fine until it is tried for real.</summary>
    static HotkeyStatuses WhilePaused(Chord? toggle, Chord? pin) => new(Unregistered(toggle), Unregistered(pin));

    static HotkeyStatus Unregistered(Chord? chord) => chord is null ? HotkeyStatus.Off : HotkeyStatus.Ok;

    /// <summary>Registers both chords afresh.</summary>
    HotkeyStatuses Register()
    {
        UnregisterAll();
        return new(Register(HotkeyAction.Toggle, _toggle), Register(HotkeyAction.PinSelection, _pin));
    }

    HotkeyStatus Register(HotkeyAction action, Chord? chord)
    {
        if (chord is not { } on)
        {
            return HotkeyStatus.Off;
        }
        if (PInvoke.RegisterHotKey((HWND)_host.Handle, (int)action, Modifiers(on), on.Vk))
        {
            return HotkeyStatus.Ok;
        }
        DevTrace.Write($"hotkeys: cannot register {on}: {Marshal.GetLastPInvokeErrorMessage()}");
        return HotkeyStatus.InUse;
    }

    void UnregisterAll()
    {
        PInvoke.UnregisterHotKey((HWND)_host.Handle, (int)HotkeyAction.Toggle);
        PInvoke.UnregisterHotKey((HWND)_host.Handle, (int)HotkeyAction.PinSelection);
    }

    /// <summary>The chord's modifiers; holding the chord down does not repeat the press.</summary>
    internal static HOT_KEY_MODIFIERS Modifiers(Chord chord)
    {
        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (chord.Win)
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_WIN;
        }
        if (chord.Ctrl)
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        }
        if (chord.Alt)
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_ALT;
        }
        if (chord.Shift)
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        }
        return modifiers;
    }

    public void Dispose() => UnregisterAll();
}
