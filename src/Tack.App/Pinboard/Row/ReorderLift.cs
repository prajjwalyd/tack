using Microsoft.UI.Xaml.Controls;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// A print travelling further than a step along the row (a keep, a new order)
/// lifts a little on the way, above the prints it passes, and settles as it
/// arrives. The lift is on the print's drop layer, over one slide's time.
/// </summary>
sealed class ReorderLift(BoardContext board)
{
    /// <summary>Above resting prints, below a hovered one.</summary>
    public const int MovingZ = 18;

    /// <summary>How much further than its own width a print must travel to lift.</summary>
    const double Step = 30;

    readonly HashSet<BoardItem> _lifting = [];

    /// <summary>Whether a print that moved <paramref name="dx"/> along the row lifts on the way.</summary>
    public static bool Lifts(BoardItem item, double dx) => Math.Abs(dx) > item.W + Step;

    public void Lift(BoardItem item)
    {
        var ms = board.Clock.LayoutMs;
        var motion = new Timeline(ms, easing: CubicBezier.EaseInOut)
            .Key(0, Pose.None)
            .Key(0.45, new Pose { Y = -5, Scale = 1.03 })
            .Key(1, Pose.None)
            .Sample();
        var slot = item.View.Slot.Element;
        Canvas.SetZIndex(slot, MovingZ);
        _lifting.Add(item);
        board.Keyframer.Play(motion, item.View.Drop.Visual);
        board.Timers.Later(ms, () => Land(item));
    }

    /// <summary>Every lift cut short by a tuck is put down, once the motions are cancelled.</summary>
    public void Settle()
    {
        foreach (var item in _lifting.ToArray())
        {
            board.Keyframer.Set(item.View.Drop.Visual, Channel.Y, 0);
            board.Keyframer.Set(item.View.Drop.Visual, Channel.Scale, 1);
            Land(item);
        }
    }

    void Land(BoardItem item)
    {
        _lifting.Remove(item);
        var slot = item.View.Slot.Element;
        // Only if nothing has raised it further since (a hover, an arrival).
        if (Canvas.GetZIndex(slot) == MovingZ)
        {
            Canvas.SetZIndex(slot, 0);
        }
    }
}
