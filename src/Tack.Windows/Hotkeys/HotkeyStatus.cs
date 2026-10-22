namespace Tack.Windows.Hotkeys;

/// <summary>How registering one shortcut went.</summary>
public enum HotkeyStatus
{
    /// <summary>Registered: it works.</summary>
    Ok,

    /// <summary>Turned off.</summary>
    Off,

    /// <summary>Another app has it (or Windows keeps it for itself).</summary>
    InUse,
}
