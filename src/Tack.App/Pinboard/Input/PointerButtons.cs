using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Tack.App.Pinboard.Model;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// A print's Keep and unpin buttons act on a click: the left button down and
/// up on the same button. They stay out of the press gestures, so a press on
/// one never copies, edits or drags the print. They take the pointer only
/// while shown (the print's view sees to that).
/// </summary>
static class PointerButtons
{
    /// <summary>Keep toggles keeping <paramref name="item"/>; the cross unpins it.</summary>
    public static void Wire(BoardItem item, BoardContext board)
    {
        Wire(item.View.KeepButton.Element, item, () => board.Actions.SetKept(item.Id, !item.Print.Kept));
        Wire(item.View.XButton.Element, item, () => board.Actions.Discard(item.Id));
    }

    static void Wire(FrameworkElement button, BoardItem item, Action act)
    {
        var armed = false;
        button.PointerPressed += (_, e) =>
        {
            // Handled, so the print under the button never starts a press.
            e.Handled = true;
            var kind = e.GetCurrentPoint(button).Properties.PointerUpdateKind;
            armed = kind == PointerUpdateKind.LeftButtonPressed && button.CapturePointer(e.Pointer);
        };
        button.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            var point = e.GetCurrentPoint(button);
            var click = armed
                && point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased
                && Inside(button, point.Position.X, point.Position.Y);
            armed = false;
            button.ReleasePointerCapture(e.Pointer);
            if (click && !item.Leaving)
            {
                act();
            }
        };
        button.PointerCanceled += (_, _) => armed = false;
    }

    static bool Inside(FrameworkElement button, double x, double y) =>
        x >= 0 && y >= 0 && x <= button.ActualWidth && y <= button.ActualHeight;
}
