using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Tack.App.Pinboard.Model;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// The keyboard's roving focus on the row: one print at a time is the row's
/// only tab stop and its selection, or, with none, the board itself. The
/// print wears the focus ring only while the keyboard drives (an empty board
/// rings its hint card). The focus is remembered across tucks. UI thread only.
/// </summary>
sealed class KeyboardFocus(BoardContext board)
{
    string? _id;
    bool _ringed;

    /// <summary>The focused print, unless it is leaving.</summary>
    public BoardItem? Item => _id is { } id && board.State.Find(id) is { Leaving: false } item ? item : null;

    /// <summary>Whether <paramref name="item"/> holds the focus, leaving or not.</summary>
    public bool Holds(BoardItem item) => item.Id == _id;

    /// <summary>Whether the focus ring shows: the keyboard is driving.</summary>
    public bool Ringed
    {
        get => _ringed;
        set
        {
            _ringed = value;
            Ring(Item);
        }
    }

    /// <summary>Moves the focus to <paramref name="item"/>, or to the board itself (null). The keyboard follows through <see cref="Focus"/>.</summary>
    public void Rove(BoardItem? item)
    {
        var old = _id is { } id ? board.State.Find(id) : null;
        if (old is not null && old != item)
        {
            old.View.Slot.Element.IsTabStop = false;
            old.View.SetFocused(false);
        }
        _id = item?.Id;
        board.Layers.Board.IsTabStop = item is null;
        if (item is not null)
        {
            item.View.Slot.Element.IsTabStop = true;
        }
        Ring(item);
        if (old != item)
        {
            board.Parts.Automation.SelectionMoved(old, item);
        }
    }

    /// <summary>Gives the keyboard to the focused print, or the board; whether it took it.</summary>
    public bool Focus() => Target().Focus(FocusState.Keyboard);

    /// <summary>Whether <paramref name="item"/> has the keyboard right now.</summary>
    public bool HasKeyboard(BoardItem item) =>
        board.Layers.Root.XamlRoot is { } root
        && ReferenceEquals(FocusManager.GetFocusedElement(root), item.View.Slot.Element);

    /// <summary>Lets go of the keyboard: nothing on the board is a tab stop until the next <see cref="Rove"/>.</summary>
    public void Blur()
    {
        if (_id is { } id && board.State.Find(id) is { } item)
        {
            item.View.Slot.Element.IsTabStop = false;
        }
        board.Layers.Board.IsTabStop = false;
    }

    UIElement Target() => Item?.View.Slot.Element ?? (UIElement)board.Layers.Board;

    void Ring(BoardItem? item)
    {
        item?.View.SetFocused(_ringed);
        board.Parts.Row.RingHint(_ringed && item is null);
    }
}
