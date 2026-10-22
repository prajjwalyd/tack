using Microsoft.UI.Input;
using Tack.Core.Pinboard.Input;

namespace Tack.App.Pinboard.Input;

/// <summary>Which button a pointer event is about, for the gestures.</summary>
static class PointerKind
{
    /// <summary>The button that went down or came up (a touch or a pen tip counts as the left one).</summary>
    public static PointerButton Of(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => PointerButton.Left,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => PointerButton.Right,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => PointerButton.Middle,
        _ => PointerButton.Other,
    };
}
