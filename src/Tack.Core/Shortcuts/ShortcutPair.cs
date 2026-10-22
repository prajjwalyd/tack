namespace Tack.Core.Shortcuts;

/// <summary>Both shortcuts: show or hide the board, and pin the selection.</summary>
public sealed record ShortcutPair(ShortcutState Toggle, ShortcutState Pin)
{
    /// <summary>Neither is taken nor invalid, so the pair can be kept.</summary>
    public bool AllWork => Works(Toggle) && Works(Pin);

    static bool Works(ShortcutState shortcut) => shortcut.Status is ShortcutStatus.Ok or ShortcutStatus.Off;
}
