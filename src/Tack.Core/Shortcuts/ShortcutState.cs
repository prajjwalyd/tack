namespace Tack.Core.Shortcuts;

/// <summary>One shortcut as the Shortcuts section shows it.</summary>
/// <param name="Chord">"Win+Alt+S", empty when off.</param>
public sealed record ShortcutState(string Chord, ShortcutStatus Status)
{
    /// <summary>The line under a shortcut's row: what is wrong with it, or empty.</summary>
    public static string Describe(ShortcutState shortcut) => shortcut.Status switch
    {
        ShortcutStatus.InUse when shortcut.Chord.Length > 0 => $"{shortcut.Chord} is in use by another app",
        ShortcutStatus.InUse => "In use by another app",
        ShortcutStatus.Invalid => ChordCapture.Hint,
        _ => string.Empty,
    };
}
