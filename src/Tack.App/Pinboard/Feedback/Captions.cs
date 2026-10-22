using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Motion;
using Tack.Core.Pinboard.Words;
using Tack.Core.Prints;

namespace Tack.App.Pinboard.Feedback;

/// <summary>
/// The age under the hovered print ("Just now", "2 minutes ago",
/// "Yesterday"), kept fresh while it shows: under an hour old just after each
/// minute of its age, else every five minutes. Only the hovered print's
/// caption has a timer, and none runs while tucked. The pointer input calls
/// <see cref="Show"/> and <see cref="Hide"/> as a print gains and loses the
/// hover look; the print's view draws the words. UI thread only.
/// </summary>
sealed class Captions(BoardContext board) : BoardPart(board)
{
    BoardItem? _shown;
    DispatcherQueueTimer? _refresh;

    /// <summary>The age of <paramref name="item"/> as its caption says it now.</summary>
    public static string TextFor(BoardItem item) => Age.Caption(item.PinnedAt, Now());

    /// <summary><paramref name="item"/> has the hover look: its caption gets its age and keeps it current.</summary>
    public void Show(BoardItem item)
    {
        Stop();
        if (item.Leaving || !Board.State.Revealed)
        {
            return;
        }
        _shown = item;
        item.View.SetAge(TextFor(item));
        var next = Age.RefreshMs(item.PinnedAt, Now());
        _refresh = Board.Timers.Later(MotionClock.Fixed(next, "the age follows the clock, not the motion"), () =>
        {
            _refresh = null;
            if (_shown == item && Board.State.Hovered == item)
            {
                Show(item);
            }
        });
    }

    /// <summary><paramref name="item"/> lost the hover look: its caption no longer refreshes.</summary>
    public void Hide(BoardItem item)
    {
        if (_shown == item)
        {
            Stop();
        }
    }

    public override void Updated(BoardItem item, Print old)
    {
        if (_shown == item)
        {
            Show(item);
        }
    }

    public override void Removed(BoardItem item, Removal how) => Hide(item);

    public override void PointerLeft()
    {
        if (_shown is { } item && Board.State.Hovered != item)
        {
            Stop();
        }
    }

    public override void Tucking() => Stop();

    void Stop()
    {
        Board.Timers.Cancel(_refresh);
        _refresh = null;
        _shown = null;
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
