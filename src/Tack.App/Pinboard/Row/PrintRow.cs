using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Layout;
using Tack.Core.Prints;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// The row: each print's size and place, the board's width, new prints'
/// tilt and pin colour, and when they pin on. One row, kept prints first; the
/// board fits it up to the window less <see cref="BoardMetrics.Side"/>, and
/// prints are placed from the board's centre, so a width change never moves
/// the ones already there. Prints glide to new places and the cork slides to
/// a new width; an empty board shows the hint card. The app's new orders wait
/// for a keep press (<see cref="HoldOrder"/>), and prints travelling far lift
/// on the way.
/// </summary>
sealed class PrintRow : BoardPart
{
    /// <summary>After a print starts to fall, before the row closes its gap. Class A.</summary>
    const double CloseGapMs = 120;

    readonly SlotGlides _glides;
    readonly CorkSlide _cork;
    readonly HintCard _hint;
    readonly ReorderLift _lift;
    readonly OrderHold _order;
    readonly Arrivals _arrivals;
    bool _gapPending;

    public PrintRow(BoardContext board) : base(board)
    {
        _glides = new SlotGlides(board.Keyframer);
        _cork = new CorkSlide(board.Layers, board.Keyframer);
        _hint = new HintCard(board);
        _lift = new ReorderLift(board);
        _order = new OrderHold(board, ApplyOrder);
        _arrivals = new Arrivals(board, this);
    }

    /// <summary>The row as last laid out, in <see cref="BoardState.Order"/>: places, the board's width, the scroll extent.</summary>
    public RowLayout Current { get; private set; } = new([], 0);

    BoardState State => Board.State;

    /// <summary>A new print's board item: its layers, a resting tilt either way and a pin colour from its id.</summary>
    public BoardItem Create(Print print)
    {
        var tilt = Tilt.Next(Board.Random);
        var item = new BoardItem(print, Board.NewView()) { Tilt = tilt, PinColor = PinPalette.ColorFor(print.Id) };
        item.View.Update(print);
        item.View.SetTilt(tilt);
        item.View.SetPinColor(item.PinColor);
        Measure(item);
        return item;
    }

    /// <summary>
    /// Lays the row out and sizes the board for it. Prints glide and the cork
    /// slides unless <paramref name="instant"/>, the board is tucked or less
    /// motion is asked for; a print placed for the first time never slides.
    /// </summary>
    public void Layout(bool instant = false)
    {
        _gapPending = false;
        var live = State.Row.ToList();
        var layout = new RowLayout([.. live.Select(item => new RowEntry(item.W, item.H, item.Print.Kept))], State.WindowWidth);
        double? glideMs = !instant && State.Revealed && !State.Reduced ? Board.Clock.LayoutMs : null;
        for (var i = 0; i < live.Count; i++)
        {
            _glides.Place(live[i], layout.X[i], RowLayout.Y, glideMs);
        }
        FixPinColors(live);
        var before = Board.Geometry.Width;
        if (layout.BoardWidth != before)
        {
            Board.Geometry.SetWidth(layout.BoardWidth);
            _hint.Fit(layout.BoardWidth);
            _cork.Resize(before, layout.BoardWidth, glideMs);
        }
        Current = layout;
        Board.Parts.Scroller.SetExtent(layout.ScrollExtent);
        _hint.Show(live.Count == 0);
        Board.Geometry.SendRect(instant);
    }

    /// <summary>
    /// Brings <paramref name="item"/> into view if the row scrolls: true if it
    /// had to move (more than a pixel) to show it.
    /// </summary>
    public bool ShowPrint(BoardItem item)
    {
        var index = State.Order.IndexOf(item.Id);
        if (index < 0 || index >= Current.Count)
        {
            return false;
        }
        var from = Board.Parts.Scroller.Target;
        var to = Current.ScrollToShow(index, from);
        if (Math.Abs(to - from) <= 1)
        {
            return false;
        }
        Board.Parts.Scroller.ScrollTo(to);
        return true;
    }

    /// <summary>Where the row rests when the board comes down, for the scroller.</summary>
    public double RestScroll() => Current.RestScroll();

    /// <summary>Where <paramref name="item"/>'s menu opens: its bottom-left corner on the window, physical px.</summary>
    public (int X, int Y) MenuPoint(BoardItem item)
    {
        var index = Math.Max(0, State.Order.IndexOf(item.Id));
        return index < Current.Count
            ? Current.MenuPoint(index, State.ScrollPosition, State.WindowWidth, State.Scale)
            : (0, 0);
    }

    /// <summary>The keyboard's focus ring around the empty board's hint card, or not.</summary>
    public void RingHint(bool on) => _hint.SetRinged(on);

    /// <summary>Holds the app's new orders back for <paramref name="ms"/> (already scaled): a keep press is seen before the print moves.</summary>
    public void HoldOrder(double ms) => _order.Hold(ms);

    /// <summary>Ends a hold and applies the order waiting on it, if any.</summary>
    public void FlushOrder() => _order.Flush();

    /// <summary>Whether <paramref name="item"/> waits hidden for its pin-on or flight.</summary>
    public bool IsHidden(BoardItem item) => _arrivals.IsHidden(item);

    /// <summary>Keeps <paramref name="item"/> out of sight, still in its place, until <see cref="Unhide"/> (a flight in the air).</summary>
    public void Hide(BoardItem item) => _arrivals.Hide(item);

    /// <summary>Shows a hidden print; false if it was not hidden.</summary>
    public bool Unhide(BoardItem item) => _arrivals.Unhide(item);

    public override void Synced() => Layout(instant: true);

    public override void Revealed(RevealReason reason, bool wasRevealed) => _arrivals.Revealed(wasRevealed);

    public override void Tucking()
    {
        _order.Flush();
        if (_gapPending)
        {
            Layout();
        }
        _arrivals.Tucking();
        Board.Geometry.StopRectTimers();
    }

    public override void Tucked()
    {
        _glides.Settle();
        _cork.Settle();
        _lift.Settle();
        _hint.Settle();
    }

    public override void Added(BoardItem item, bool animate, Flight? flight)
    {
        var widthBefore = Board.Geometry.Width;
        Layout();
        if (animate)
        {
            _arrivals.Arrived(item, widthBefore);
        }
    }

    public override void Removed(BoardItem item, Removal how)
    {
        _glides.Forget(item);
        _arrivals.Forget(item);
        if (State.Revealed && !State.Reduced && how == Removal.Fall)
        {
            // Let the print start to go before the row closes the gap.
            _gapPending = true;
            Board.Timers.Later(Board.Clock.Scaled(CloseGapMs), () => Layout());
            return;
        }
        Layout();
    }

    public override void Updated(BoardItem item, Print old)
    {
        if (old.IsNote != item.Print.IsNote || old.Width != item.Print.Width || old.Height != item.Print.Height)
        {
            Measure(item);
            Layout();
        }
    }

    public override void Reordered(IReadOnlyList<string> ids) => _order.Offer(ids);

    public override void Resized()
    {
        Layout(instant: true);
        if (!State.Revealed)
        {
            Board.Parts.Scroller.Rest();
        }
    }

    public override void ThemeChanged() => _hint.ApplyTheme();

    /// <summary>The app's new row order. Prints that travel further than a step lift on the way.</summary>
    void ApplyOrder(IReadOnlyList<string> ids)
    {
        var next = RowOrder.Merge(State.Order, ids, id => State.Find(id) is { Leaving: false });
        if (next.SequenceEqual(State.Order))
        {
            return;
        }
        var before = State.Row.ToDictionary(item => item, item => item.X);
        State.Order.Clear();
        State.Order.AddRange(next);
        Layout();
        if (!State.Revealed)
        {
            return;
        }
        if (State.Hovered is { } hovered && before.TryGetValue(hovered, out var x) && hovered.X != x)
        {
            // It moved from under the pointer: hover again whatever is there once the row has moved.
            Board.Parts.Gestures.HoveredMoved();
        }
        if (State.Reduced)
        {
            return;
        }
        foreach (var item in State.Row)
        {
            if (before.TryGetValue(item, out var from) && ReorderLift.Lifts(item, item.X - from))
            {
                _lift.Lift(item);
            }
        }
    }

    /// <summary>Neighbouring unkept pins never share a colour; on a clash the newer print changes.</summary>
    static void FixPinColors(List<BoardItem> live)
    {
        var colors = PinPalette.Fix([.. live.Select(item => new PinSlot(item.PinColor, item.Print.Kept, item.Settled))]);
        for (var i = 0; i < live.Count; i++)
        {
            if (live[i].PinColor != colors[i])
            {
                live[i].PinColor = colors[i];
                live[i].View.SetPinColor(colors[i]);
            }
            live[i].Settled = true;
        }
    }

    /// <summary>A print's size: a note is one size; a photo fits 160 by 100, plus its paper border.</summary>
    static void Measure(BoardItem item)
    {
        (item.W, item.H) = PrintSize.Of(item.Print.IsNote, item.Print.Width, item.Print.Height);
        item.View.Resize(item.W, item.H);
    }
}
