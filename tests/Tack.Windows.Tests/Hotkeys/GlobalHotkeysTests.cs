using Tack.Core.Shortcuts;
using Tack.Windows.Host;
using Tack.Windows.Hotkeys;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Tack.Windows.Tests.Hotkeys;

/// <summary>
/// Registers real global shortcuts, but only chords no one presses (all four
/// modifiers and F23 or F24), and always lets them go again.
/// </summary>
[Collection("HostWindow")]
public sealed class GlobalHotkeysTests
{
    const int TestId = 0x7A11;

    static readonly Chord Rare = Chord(0x87);
    static readonly Chord Other = Chord(0x86);

    static Chord Chord(ushort vk) => new(Win: true, Ctrl: true, Alt: true, Shift: true, vk);

    [Fact]
    public void A_free_chord_works_and_none_is_off()
    {
        using var host = new HostWindow();
        using var hotkeys = new GlobalHotkeys(host);

        Assert.Equal(new HotkeyStatuses(HotkeyStatus.Ok, HotkeyStatus.Off), hotkeys.Set(Rare, null));
        Assert.False(Free(Rare));
    }

    [Fact]
    public void A_chord_held_elsewhere_is_in_use()
    {
        using var host = new HostWindow();
        using var hotkeys = new GlobalHotkeys(host);
        Assert.True(Take(Rare));
        try
        {
            var statuses = hotkeys.Set(Rare, Other);
            Assert.Equal(new HotkeyStatuses(HotkeyStatus.InUse, HotkeyStatus.Ok), statuses);
            Assert.True(statuses.AnyInUse);
        }
        finally
        {
            PInvoke.UnregisterHotKey(HWND.Null, TestId);
        }
    }

    [Fact]
    public void Both_on_one_chord_leaves_the_second_in_use()
    {
        using var host = new HostWindow();
        using var hotkeys = new GlobalHotkeys(host);

        Assert.Equal(new HotkeyStatuses(HotkeyStatus.Ok, HotkeyStatus.InUse), hotkeys.Set(Rare, Rare));
    }

    [Fact]
    public void A_changed_chord_lets_the_old_one_go()
    {
        using var host = new HostWindow();
        using var hotkeys = new GlobalHotkeys(host);
        hotkeys.Set(Rare, null);

        Assert.Equal(new HotkeyStatuses(HotkeyStatus.Off, HotkeyStatus.Ok), hotkeys.Set(null, Other));
        Assert.True(Free(Rare));
        Assert.False(Free(Other));
    }

    [Fact]
    public void Pausing_lets_the_chords_go_and_resuming_takes_them_back()
    {
        using var host = new HostWindow();
        using var hotkeys = new GlobalHotkeys(host);
        hotkeys.Set(Rare, null);

        Assert.Equal(new HotkeyStatuses(HotkeyStatus.Ok, HotkeyStatus.Off), hotkeys.Pause(true));
        Assert.True(Free(Rare));
        Assert.Equal(new HotkeyStatuses(HotkeyStatus.Ok, HotkeyStatus.Off), hotkeys.Pause(false));
        Assert.False(Free(Rare));
    }

    [Fact]
    public void A_chord_set_while_paused_is_taken_on_resuming()
    {
        using var host = new HostWindow();
        using var hotkeys = new GlobalHotkeys(host);
        hotkeys.Pause(true);

        // Counted as fine until it is tried for real.
        Assert.Equal(new HotkeyStatuses(HotkeyStatus.Off, HotkeyStatus.Ok), hotkeys.Set(null, Rare));
        Assert.True(Free(Rare));
        Assert.Equal(new HotkeyStatuses(HotkeyStatus.Off, HotkeyStatus.Ok), hotkeys.Pause(false));
        Assert.False(Free(Rare));
    }

    [Fact]
    public void Disposing_lets_the_chords_go()
    {
        using var host = new HostWindow();
        var hotkeys = new GlobalHotkeys(host);
        hotkeys.Set(Rare, Other);

        hotkeys.Dispose();
        Assert.True(Free(Rare));
        Assert.True(Free(Other));
    }

    [Fact]
    public void A_press_is_reported_as_its_action()
    {
        using var host = new HostWindow();
        using var hotkeys = new GlobalHotkeys(host);
        var pressed = new List<HotkeyAction>();
        hotkeys.Pressed += pressed.Add;

        hotkeys.OnHotkey(2);
        hotkeys.OnHotkey(1);
        // Windows' own ids, such as IDHOT_SNAPDESKTOP, are not Tack's.
        hotkeys.OnHotkey(unchecked((nuint)(-2)));

        Assert.Equal([HotkeyAction.PinSelection, HotkeyAction.Toggle], pressed);
    }

    [Fact]
    public void Modifiers_follow_the_chord_and_a_held_key_does_not_repeat()
    {
        var winAlt = new Chord(Win: true, Ctrl: false, Alt: true, Shift: false, 'S');
        Assert.Equal(
            HOT_KEY_MODIFIERS.MOD_WIN | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_NOREPEAT,
            GlobalHotkeys.Modifiers(winAlt));
        var ctrlShift = new Chord(Win: false, Ctrl: true, Alt: false, Shift: true, 0x70);
        Assert.Equal(
            HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_NOREPEAT,
            GlobalHotkeys.Modifiers(ctrlShift));
    }

    /// <summary>Takes <paramref name="chord"/> as another app would.</summary>
    static bool Take(Chord chord) => PInvoke.RegisterHotKey(HWND.Null, TestId, GlobalHotkeys.Modifiers(chord), chord.Vk);

    /// <summary>Whether no one holds <paramref name="chord"/>; it is let go again at once.</summary>
    static bool Free(Chord chord)
    {
        if (!Take(chord))
        {
            return false;
        }
        PInvoke.UnregisterHotKey(HWND.Null, TestId);
        return true;
    }
}
