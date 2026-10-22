namespace Tack.Core.Pinboard.Input;

/// <summary>What a gesture on a print asks for.</summary>
public enum GestureAction
{
    /// <summary>A click, once no second press came in time: the print onto the clipboard.</summary>
    Copy,

    /// <summary>A double click: the print in its default app.</summary>
    Open,

    /// <summary>A press held long enough: the shell's "edit".</summary>
    Edit,

    /// <summary>A press that moved far enough: the print's file is dragged out.</summary>
    StartDrag,

    /// <summary>A right click: the print's menu at the pointer.</summary>
    Menu,
}
