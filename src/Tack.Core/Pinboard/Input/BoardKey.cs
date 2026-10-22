namespace Tack.Core.Pinboard.Input;

/// <summary>The keys the board answers to; every other key is <see cref="None"/>.</summary>
public enum BoardKey
{
    None,

    Left,

    Right,

    Home,

    End,

    Enter,

    K,

    Delete,

    /// <summary>With Shift: the menu.</summary>
    F10,

    /// <summary>The menu key (the application key).</summary>
    Menu,

    Escape,

    Tab,
}
