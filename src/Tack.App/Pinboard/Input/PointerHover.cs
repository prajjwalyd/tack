using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Tack.App.Pinboard.Model;
using Tack.Core.Geometry;
using Tack.Core.Pinboard.Input;
using Windows.Foundation;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// The hover look, on one print at a time: never on a leaving print, the one
/// being dragged out, or while the row scrolls. It keeps
/// <see cref="BoardState.Hovered"/> and the hovered print's caption current,
/// leans the print toward the pointer, and remembers where the pointer last
/// was, so a print that comes under a still pointer (a scroll settling, the
/// row reordering) can be hovered without the pointer moving. UI thread only.
/// </summary>
/// <param name="slots">Each print's slot element, for finding the print under a point.</param>
sealed class PointerHover(BoardContext board, IReadOnlyDictionary<UIElement, BoardItem> slots)
{
    /// <summary>Where the pointer last was over the window, logical px; null once it left.</summary>
    public Point? Pointer { get; set; }

    /// <summary>The row is scrolling under the pointer: nothing is hovered until it settles.</summary>
    public bool Scrolling { get; set; }

    BoardState State => board.State;

    /// <summary>Gives <paramref name="item"/> the hover look, taking it from any other print.</summary>
    public void Set(BoardItem item)
    {
        if (item.Leaving || State.DraggingId == item.Id || Scrolling || State.Hovered == item)
        {
            return;
        }
        Clear();
        State.Hovered = item;
        item.View.SetHover(true);
        board.Parts.Captions.Show(item);
    }

    /// <summary><paramref name="item"/> loses the hover look, if it has it.</summary>
    public void Leave(BoardItem item)
    {
        if (State.Hovered == item)
        {
            Clear();
        }
    }

    /// <summary>Nothing is hovered any more.</summary>
    public void Clear()
    {
        if (State.Hovered is not { } item)
        {
            return;
        }
        State.Hovered = null;
        item.View.SetHover(false);
        board.Parts.Captions.Hide(item);
    }

    /// <summary>The pointer is at <paramref name="inSlot"/> over <paramref name="item"/>, relative to its slot: the hovered print leans toward it.</summary>
    public void Lean(BoardItem item, Point inSlot)
    {
        if (State.Hovered != item || State.Pressing is not null || State.Reduced || item.Leaving)
        {
            return;
        }
        // Relative to the slot, which the lean never moves: a steady frame for the whole hover.
        var lean = HoverLean.At(new LogicalRect(0, 0, item.W, item.H), inSlot.X, inSlot.Y);
        item.View.Lean(lean.Nx, lean.Ny);
    }

    /// <summary>The print whose slot is under <paramref name="point"/> (logical px on the window), if any.</summary>
    public BoardItem? At(Point point)
    {
        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(point, board.Layers.Track))
        {
            if (slots.TryGetValue(element, out var item) && !item.Leaving)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>Hovers whatever print is under the still pointer now, unless a press is held.</summary>
    public void Rehover()
    {
        if (Pointer is { } point && State.Pressing is null && State.Revealed && At(point) is { } item)
        {
            Set(item);
        }
    }
}
