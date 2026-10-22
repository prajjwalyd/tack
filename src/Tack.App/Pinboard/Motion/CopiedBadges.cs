using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// The copied check stamped on a print for a second (a readable while at any
/// speed), then going away; a copy again meanwhile stamps it afresh. UI thread only.
/// </summary>
sealed class CopiedBadges(BoardContext board)
{
    readonly Dictionary<BoardItem, DispatcherQueueTimer> _holding = [];

    public void Stamp(BoardItem item)
    {
        Drop(item);
        var (clock, keyframer, badge) = (board.Clock, board.Keyframer, item.View.Badge.Visual);
        keyframer.Play(BoardMotions.BadgeIn(clock, board.State.Reduced).Sample(), badge);
        _holding[item] = board.Timers.Later(BoardMotions.BadgeHold(clock), () =>
        {
            _holding.Remove(item);
            keyframer.Play(BoardMotions.BadgeOut(clock).Sample(), badge);
        });
    }

    /// <summary>Forgets <paramref name="item"/>'s hold (it is leaving, or the board tucked and the check is put away).</summary>
    public void Drop(BoardItem item)
    {
        if (_holding.Remove(item, out var timer))
        {
            board.Timers.Cancel(timer);
        }
    }

    /// <summary>The board has tucked: no check stays up.</summary>
    public void Clear() => _holding.Clear();
}
