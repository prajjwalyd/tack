namespace Tack.Core.Pinboard.Input;

/// <summary>What a key press does: an action and, for a move, the row index it moves to.</summary>
public readonly record struct KeyboardCommand(KeyboardAction Action, int Target = -1)
{
    /// <summary>The board takes the key, so nothing else acts on it.</summary>
    public bool Handled => Action != KeyboardAction.None;

    /// <summary>The keyboard is driving: the focused print shows its ring and hover look again (a pointer press hid them).</summary>
    public bool ShowsRing => Action is KeyboardAction.Move or KeyboardAction.Stay or KeyboardAction.Copy
        or KeyboardAction.Open or KeyboardAction.Keep;
}
