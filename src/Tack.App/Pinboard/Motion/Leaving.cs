using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// Prints on their way off the board. A fall pops the pin out with a pop and
/// tumbles the print into the overhang; a quiet removal is a short fade, as
/// is a fall under reduced motion. The print is forgotten once its motion
/// is over, or at once while tucked; a tuck forgets every print still going.
/// UI thread only.
/// </summary>
sealed class Leaving(BoardContext board)
{
    readonly HashSet<BoardItem> _going = [];

    /// <summary>Takes <paramref name="item"/> off the board as <paramref name="how"/> says.</summary>
    public void Leave(BoardItem item, Removal how)
    {
        var view = item.View;
        view.Leaving = true;
        if (!board.State.Revealed)
        {
            board.Forget(item);
            return;
        }
        _going.Add(item);
        var (clock, keyframer) = (board.Clock, board.Keyframer);
        if (how != Removal.Fall || board.State.Reduced)
        {
            var fade = how == Removal.Fall ? LeaveMotions.ReducedFall(clock) : LeaveMotions.Quiet(clock);
            keyframer.Play(LeaveMotions.PinFade(clock).Sample(), view.Pin.Visual);
            keyframer.Play(fade.Sample(), view.Drop.Visual, () => Gone(item));
            if (how == Removal.Fall)
            {
                board.Parts.Sounds.Pop();
            }
            return;
        }
        var direction = item.Tilt < 0 ? -1 : 1;
        var spin = LeaveMotions.MinSpin + board.Random.NextDouble() * (LeaveMotions.MaxSpin - LeaveMotions.MinSpin);
        keyframer.Play(LeaveMotions.FallPin(clock, direction).Sample(), view.Pin.Visual);
        board.Parts.Sounds.Pop();
        keyframer.Play(LeaveMotions.FallDrop(clock, direction, spin).Sample(), view.Drop.Visual, () => Gone(item));
    }

    /// <summary>The board has tucked: every print still on its way is gone now.</summary>
    public void ForgetAll()
    {
        foreach (var item in _going.ToArray())
        {
            Gone(item);
        }
        // A print whose motion was taken over never hears its end: forget any leaving print left.
        foreach (var item in board.State.Items.Values.Where(i => i.Leaving).ToArray())
        {
            board.Forget(item);
        }
    }

    void Gone(BoardItem item)
    {
        if (_going.Remove(item))
        {
            board.Forget(item);
        }
    }
}
