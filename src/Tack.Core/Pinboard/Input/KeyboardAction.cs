namespace Tack.Core.Pinboard.Input;

/// <summary>What a key does on the board.</summary>
public enum KeyboardAction
{
    /// <summary>Nothing: the key is left to others.</summary>
    None,

    /// <summary>The focus moves to the print at <see cref="KeyboardCommand.Target"/>.</summary>
    Move,

    /// <summary>The key is taken and the focus stays where it is (Tab stays in the row).</summary>
    Stay,

    /// <summary>The focused print onto the clipboard.</summary>
    Copy,

    /// <summary>The focused print in its default app, or a link in the browser.</summary>
    Open,

    /// <summary>The focused print is kept, or no longer kept.</summary>
    Keep,

    /// <summary>The focused print is unpinned with a fall.</summary>
    Unpin,

    /// <summary>The focused print's menu opens under it.</summary>
    Menu,

    /// <summary>The board tucks away.</summary>
    Tuck,
}
