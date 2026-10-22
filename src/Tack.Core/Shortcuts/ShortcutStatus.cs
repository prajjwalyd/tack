namespace Tack.Core.Shortcuts;

/// <summary>How a shortcut stands, as the Shortcuts section shows it.</summary>
public enum ShortcutStatus
{
    /// <summary>Registered: it works.</summary>
    Ok,

    /// <summary>Turned off.</summary>
    Off,

    /// <summary>Another app has it, or the other Tack shortcut does.</summary>
    InUse,

    /// <summary>Not a chord Tack can use.</summary>
    Invalid,
}
