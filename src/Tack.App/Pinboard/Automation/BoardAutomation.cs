using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Prints;

namespace Tack.App.Pinboard.Automation;

/// <summary>
/// The board for Narrator and other assistive tools: the board is a list
/// (<see cref="BoardListPeer"/>) of prints (<see cref="PrintItemPeer"/>), each
/// named in words, with its Keep and unpin buttons; the selection follows the
/// keyboard's focus; an empty board has a description saying how to fill it.
/// Screen readers are told when the row, a print's name or the selection
/// changes, and only while one is listening. UI thread only.
/// </summary>
sealed class BoardAutomation : BoardPart
{
    readonly PeerCanvas? _surface;

    public BoardAutomation(BoardContext board) : base(board)
    {
        if (board.Layers.Board is PeerCanvas surface)
        {
            _surface = surface;
            surface.Peer = () => new BoardListPeer(surface, this);
        }
    }

    /// <summary>The row's prints, in order.</summary>
    public IEnumerable<BoardItem> Row => Board.State.Row;

    public int Count => Board.State.Order.Count;

    public bool IsEmpty => Count == 0;

    /// <summary>The selected print: the one with the keyboard's focus.</summary>
    public BoardItem? Selected => Board.Parts.Keyboard.Focused;

    /// <summary>The board's own peer, if a screen reader has asked for it.</summary>
    public AutomationPeer? ListPeer => _surface is null ? null : FrameworkElementAutomationPeer.FromElement(_surface);

    public int IndexOf(BoardItem item) => Board.State.Order.IndexOf(item.Id);

    /// <summary><paramref name="item"/>'s peer, made if need be.</summary>
    public AutomationPeer? PeerOf(BoardItem item) => FrameworkElementAutomationPeer.CreatePeerForElement(item.View.Slot.Element);

    /// <summary>A screen reader selected or focused <paramref name="item"/>: the keyboard's focus moves there.</summary>
    public void Select(BoardItem item)
    {
        if (!item.Leaving)
        {
            Board.Parts.Keyboard.Select(item);
        }
    }

    public void Keep(BoardItem item) => Board.Parts.Keyboard.Keep(item);

    public void Unpin(BoardItem item)
    {
        if (!item.Leaving)
        {
            Board.Actions.Discard(item.Id);
        }
    }

    /// <summary>The keyboard's focus moved from <paramref name="old"/> to <paramref name="now"/>: the selection follows.</summary>
    public void SelectionMoved(BoardItem? old, BoardItem? now)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)
            && !AutomationPeer.ListenerExists(AutomationEvents.SelectionItemPatternOnElementSelected))
        {
            return;
        }
        if (old is not null && Existing(old) is { } was)
        {
            was.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, true, false);
        }
        if (now is not null && PeerOf(now) is { } peer)
        {
            peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, false, true);
            peer.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
        }
    }

    public override void Added(BoardItem item, bool animate, Flight? flight)
    {
        if (item.View.Slot.Element is PeerCanvas slot)
        {
            slot.Peer = () => new PrintItemPeer(slot, item, this);
        }
        RowChanged();
    }

    public override void Removed(BoardItem item, Removal how) => RowChanged();

    public override void Synced() => RowChanged();

    public override void Reordered(IReadOnlyList<string> ids) => RowChanged();

    public override void Updated(BoardItem item, Print old)
    {
        if (Existing(item) is PrintItemPeer peer)
        {
            peer.Refresh();
        }
        if (old.Kept != item.Print.Kept)
        {
            // A keep moves the print in the row.
            RowChanged();
        }
    }

    /// <summary>The list's items, or whether it is empty, changed.</summary>
    void RowChanged() => ListPeer?.InvalidatePeer();

    static AutomationPeer? Existing(BoardItem item) => FrameworkElementAutomationPeer.FromElement(item.View.Slot.Element);
}
