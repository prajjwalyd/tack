namespace Tack.Core.Pinboard.Input;

/// <summary>Which of the pointer's buttons went down or came up.</summary>
public enum PointerButton
{
    /// <summary>The main button (a touch or a pen tip too): the only one that presses a print.</summary>
    Left,

    /// <summary>The menu button.</summary>
    Right,

    Middle,

    /// <summary>A side button, or one the board has no use for.</summary>
    Other,
}
