namespace Tack.Core.Pinboard.Input;

/// <summary>
/// The board's keys, as a pure rule: which key does what to the row, and
/// where the focus goes when the focused print is unpinned. Left, Right, Home
/// and End move along the row (stopping at its ends) and take Shift but no
/// other modifier; Enter copies and Ctrl+Enter opens; K keeps, once per
/// press; Delete unpins; the menu key or Shift+F10 opens the menu; Esc tucks;
/// Tab never leaves the row.
/// </summary>
public static class KeyboardNav
{
    /// <summary>The board key for a Windows virtual-key code.</summary>
    public static BoardKey KeyOf(int virtualKey) => virtualKey switch
    {
        0x25 => BoardKey.Left,
        0x27 => BoardKey.Right,
        0x24 => BoardKey.Home,
        0x23 => BoardKey.End,
        0x0D => BoardKey.Enter,
        0x4B => BoardKey.K,
        0x2E => BoardKey.Delete,
        0x79 => BoardKey.F10,
        0x5D => BoardKey.Menu,
        0x1B => BoardKey.Escape,
        0x09 => BoardKey.Tab,
        _ => BoardKey.None,
    };

    /// <summary>
    /// What <paramref name="key"/> does, held with <paramref name="modifiers"/>
    /// (<paramref name="repeat"/>: held down, the key repeating), with the
    /// print at <paramref name="focused"/> focused (-1: none) in a row of
    /// <paramref name="count"/>.
    /// </summary>
    public static KeyboardCommand Decide(BoardKey key, KeyModifiers modifiers, bool repeat, int focused, int count)
    {
        // Shift is allowed wherever a plain key is asked for.
        var plain = (modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win)) == 0;
        var has = focused >= 0 && focused < count;
        return key switch
        {
            BoardKey.Left when plain && count > 0 => Move(Step(focused, -1, count)),
            BoardKey.Right when plain && count > 0 => Move(Step(focused, 1, count)),
            BoardKey.Home when plain && count > 0 => Move(0),
            BoardKey.End when plain && count > 0 => Move(count - 1),
            BoardKey.Tab => has || count == 0 ? new(KeyboardAction.Stay) : Move(0),
            BoardKey.Escape => new(KeyboardAction.Tuck),
            BoardKey.Enter when has && (modifiers & (KeyModifiers.Alt | KeyModifiers.Win)) == 0 =>
                new((modifiers & KeyModifiers.Ctrl) != 0 ? KeyboardAction.Open : KeyboardAction.Copy),
            BoardKey.Delete when has && plain => new(KeyboardAction.Unpin),
            BoardKey.Menu when has => new(KeyboardAction.Menu),
            BoardKey.F10 when has && plain && (modifiers & KeyModifiers.Shift) != 0 => new(KeyboardAction.Menu),
            BoardKey.K when has && plain && !repeat => new(KeyboardAction.Keep),
            _ => new(KeyboardAction.None),
        };
    }

    /// <summary>
    /// The focused print at <paramref name="index"/> was unpinned, leaving
    /// <paramref name="remaining"/> in the row: the index the focus goes to,
    /// the next print (now at its place), else the previous; -1 if none is left.
    /// </summary>
    public static int AfterRemoval(int index, int remaining) =>
        remaining <= 0 ? -1 : Math.Clamp(index, 0, remaining - 1);

    static KeyboardCommand Move(int target) => new(KeyboardAction.Move, target);

    /// <summary>One step from <paramref name="focused"/>, within the row; from no focus, the first print.</summary>
    static int Step(int focused, int step, int count) =>
        focused < 0 ? 0 : Math.Clamp(focused + step, 0, count - 1);
}
