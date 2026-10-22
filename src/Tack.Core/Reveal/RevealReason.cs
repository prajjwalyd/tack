namespace Tack.Core.Reveal;

/// <summary>Why the board came down.</summary>
public enum RevealReason
{
    /// <summary>The pointer rested against the top edge.</summary>
    Edge,

    /// <summary>The show-or-hide shortcut: a keyboard open, so the board takes keyboard focus.</summary>
    Hotkey,

    /// <summary>A new screenshot arrived; the board peeks out briefly to show it.</summary>
    New,

    /// <summary>The tray icon or its menu.</summary>
    Tray,

    /// <summary>The Settings window took the keyboard focus: it holds the board down, never the keyboard.</summary>
    Settings,
}
