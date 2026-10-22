namespace Tack.Windows.Hotkeys;

/// <summary>What a global shortcut does. The values are the ids the shortcuts are registered under.</summary>
public enum HotkeyAction
{
    /// <summary>Show or hide the board.</summary>
    Toggle = 1,

    /// <summary>Pin the current selection.</summary>
    PinSelection = 2,
}
