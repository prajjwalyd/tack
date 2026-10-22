using Microsoft.UI.Xaml;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Layout;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// When new prints pin on. One that arrives while the board is down pins on
/// at once, or flies in if it was just taken; if the board had to grow or the
/// row to scroll for it, it waits hidden until there is cork under it. One
/// that arrives while tucked waits hidden (<see cref="BoardState.Awaiting"/>);
/// at the next reveal only the newest of those pins on, once the board is at
/// rest (or flies in alongside the swing), and the others simply appear.
/// </summary>
sealed class Arrivals(BoardContext board, PrintRow row)
{
    /// <summary>Of a slide, how much passes before a print waiting for room pins on.</summary>
    const double RoomShare = 0.4;

    readonly HashSet<BoardItem> _hidden = [];

    BoardState State => board.State;

    /// <summary>Whether the print waits hidden for its pin-on or flight.</summary>
    public bool IsHidden(BoardItem item) => _hidden.Contains(item);

    /// <summary>Keeps the print out of sight (still laid out) until its pin-on or flight shows it.</summary>
    public void Hide(BoardItem item)
    {
        _hidden.Add(item);
        item.View.Slot.Element.Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows a hidden print. False if it was not hidden.</summary>
    public bool Unhide(BoardItem item)
    {
        if (!_hidden.Remove(item))
        {
            return false;
        }
        item.View.Slot.Element.Visibility = Visibility.Visible;
        return true;
    }

    /// <summary>A print came in to be pinned on, after the row was laid out with it; the board was <paramref name="widthBefore"/> wide.</summary>
    public void Arrived(BoardItem item, double widthBefore)
    {
        if (!State.Revealed)
        {
            Hide(item);
            State.Awaiting.Add(item);
            return;
        }
        var moved = row.ShowPrint(item);
        if (item.Flight is not null)
        {
            FlyIn(item);
            return;
        }
        if ((State.BoardWidth > widthBefore || moved) && !State.Reduced)
        {
            Hide(item);
            board.Timers.Later(board.Clock.LayoutMs * RoomShare, () => PinOnIfStill(item));
            return;
        }
        PinOn(item);
    }

    /// <summary>The board comes down (or was down, <paramref name="wasRevealed"/>): the backlog shows, its newest pinning on.</summary>
    public void Revealed(bool wasRevealed)
    {
        var backlog = State.Awaiting.Where(item => !item.Leaving).ToList();
        State.Awaiting.Clear();
        var index = RowOrder.Newest([.. backlog.Select(item => (item.PinnedAt, item.AddedAt))]);
        if (index < 0)
        {
            return;
        }
        var newest = backlog[index];
        foreach (var item in backlog.Where(item => item != newest))
        {
            item.Flight = null;
            Unhide(item);
        }
        row.ShowPrint(newest);
        if (newest.Flight is not null)
        {
            FlyIn(newest);
            return;
        }
        board.Timers.Later(wasRevealed ? 0 : board.Clock.RestMs(State.Reduced), () => PinOnIfStill(newest));
    }

    /// <summary>The board goes up: prints whose pin-on had not run yet pin on at the next reveal instead.</summary>
    public void Tucking()
    {
        foreach (var item in State.Row.Where(item => _hidden.Contains(item) && !State.Awaiting.Contains(item)))
        {
            State.Awaiting.Add(item);
        }
    }

    public void Forget(BoardItem item) => _hidden.Remove(item);

    void PinOnIfStill(BoardItem item)
    {
        if (!item.Leaving)
        {
            PinOn(item);
        }
    }

    /// <summary>The pin-on itself is the print motions' part; the print shows as it starts.</summary>
    void PinOn(BoardItem item)
    {
        Unhide(item);
        board.Parts.PrintMotions.PinOn(item);
    }

    /// <summary>
    /// The flight is the flights' part: it keeps the print hidden until it
    /// lands (<see cref="PrintRow.Hide"/>, <see cref="PrintRow.Unhide"/>), and
    /// pins it on plainly under reduced motion or if it never takes off.
    /// </summary>
    void FlyIn(BoardItem item)
    {
        board.Parts.Flights.FlyIn(item);
    }
}
