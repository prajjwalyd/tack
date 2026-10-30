using Microsoft.UI.Dispatching;
using Tack.App.Pinboard.Model;
using Tack.App.Pinboard.Prints;
using Tack.Windows.Display;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// Which prints have views. A view is some fifty elements with the print's
/// picture and art, so a print has one only while it is near the part of the
/// row in view; the rest of a long row costs only its places. While the board
/// is down views are only ever made, as the row scrolls or grows, never
/// dropped, so no hover, press, drag or motion loses its print's layers. Once
/// the board has tucked and every motion is at rest, and until it comes down
/// again, exactly the prints in view have views, so the next reveal draws at
/// once; the keyboard's print and prints waiting hidden to pin on keep theirs
/// too. UI thread only.
/// </summary>
sealed class PrintViews(BoardContext board, PrintRow row)
{
    /// <summary>While the board is down, prints this share of the board's width past either side get their views, so a scroll finds them made.</summary>
    const double AheadShare = 0.5;

    /// <summary>Tucked with every motion over (or not yet down since the start): nothing on the board is seen or moving.</summary>
    bool _resting = true;

    bool _matchQueued;
    bool _collectQueued;

    BoardState State => board.State;

    /// <summary><paramref name="item"/>'s view, made now (in its place, showing the print) if it has none.</summary>
    public PrintView Wake(BoardItem item)
    {
        if (item.View is { } view)
        {
            return view;
        }
        view = board.NewView();
        item.View = view;
        view.Update(item.Print);
        view.SetTilt(item.Tilt);
        view.SetPinColor(item.PinColor);
        view.Resize(item.W, item.H);
        view.PlaceAt(item.X, item.Y);
        board.Layers.Track.Children.Add(view.Slot.Element);
        board.Parts.Each(part => part.Woke(item));
        return view;
    }

    /// <summary>The row moved or was laid out: every print near the view gets its view (and, at rest, only those keep one).</summary>
    public void Follow()
    {
        if (_resting)
        {
            // Prints come in bursts while tucked (last session's, a backlog): matched once, after the burst.
            _matchQueued = _matchQueued || Later(() =>
            {
                _matchQueued = false;
                if (_resting)
                {
                    Match();
                }
            });
            return;
        }
        foreach (var item in Near(AheadShare))
        {
            Wake(item);
        }
    }

    /// <summary>The board has tucked, its motions at rest and the row where it rests: views out of view go.</summary>
    public void Rest()
    {
        _resting = true;
        Match();
    }

    /// <summary>The board comes down: from now until it rests again, views are only made, starting with those in view now.</summary>
    public void Revealed()
    {
        _resting = false;
        Follow();
    }

    /// <summary>Exactly the prints in view have views, besides those <see cref="Held"/>.</summary>
    void Match()
    {
        var near = Near(0).ToHashSet();
        var dropped = false;
        foreach (var item in State.Row.ToList())
        {
            if (near.Contains(item))
            {
                Wake(item);
            }
            else if (item.View is { } view && !Held(item))
            {
                board.Parts.Each(part => part.Sleeping(item));
                board.Layers.Track.Children.Remove(view.Slot.Element);
                item.View = null;
                dropped = true;
            }
        }
        // A dropped view's elements are freed only once their wrappers are collected, which an idle app may not do for long.
        if (dropped)
        {
            _collectQueued = _collectQueued || Later(() =>
            {
                _collectQueued = false;
                GC.Collect();
            });
        }
    }

    /// <summary>Runs <paramref name="run"/> on the UI thread once nothing more pressing waits; whether it was queued.</summary>
    bool Later(DispatcherQueueHandler run) => board.Queue.TryEnqueue(DispatcherQueuePriority.Low, run);

    /// <summary>The prints within <paramref name="share"/> of the board's width of the view, as the row is now.</summary>
    IEnumerable<BoardItem> Near(double share)
    {
        var layout = row.Current;
        var prints = State.Row.Take(layout.Count);
        if (State.WindowWidth <= 0)
        {
            // Not laid out yet (the window is made hidden), so the whole row seems to fit; the board can
            // be no wider than the widest monitor, whose physical px are at least as many as its logical ones.
            var widest = Monitors.All().Select(monitor => monitor.W).DefaultIfEmpty(int.MaxValue).Max();
            return prints.Where((_, index) => layout.Span(index).Left < widest).ToList();
        }
        var margin = layout.BoardWidth * share;
        return prints.Where((_, index) => layout.InView(index, State.ScrollPosition, margin)).ToList();
    }

    /// <summary>A print that keeps its view while tucked: the keyboard's, or one waiting hidden to pin on.</summary>
    bool Held(BoardItem item) => row.IsHidden(item) || State.Awaiting.Contains(item) || board.Parts.Keyboard.Focused == item;
}
