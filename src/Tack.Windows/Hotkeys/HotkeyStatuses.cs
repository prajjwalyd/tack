namespace Tack.Windows.Hotkeys;

/// <summary>How registering both shortcuts went.</summary>
public readonly record struct HotkeyStatuses(HotkeyStatus Toggle, HotkeyStatus Pin)
{
    /// <summary>Either shortcut is taken: the caller puts the old ones back.</summary>
    public bool AnyInUse => Toggle == HotkeyStatus.InUse || Pin == HotkeyStatus.InUse;
}
