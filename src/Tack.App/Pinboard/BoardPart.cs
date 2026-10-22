using Tack.App.Pinboard.Contract;
using Tack.App.Pinboard.Model;
using Tack.Core.Prints;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard;

/// <summary>
/// A part of the board (the row, the chrome, the motions, the input, the
/// feedback) that follows the board's life. The controller calls every part,
/// in <see cref="BoardParts.All"/> order, after it has updated the
/// <see cref="BoardState"/>; a part overrides what it cares about. UI thread only.
/// </summary>
abstract class BoardPart(BoardContext board)
{
    protected BoardContext Board { get; } = board;

    /// <summary>The prints the app already had came in (each through <see cref="Added"/>, not animated).</summary>
    public virtual void Synced()
    {
    }

    /// <summary>The board comes down, or (<paramref name="wasRevealed"/>) was already down and is asked again.</summary>
    public virtual void Revealed(RevealReason reason, bool wasRevealed)
    {
    }

    /// <summary>The board starts going up; the board's timers are cancelled right after.</summary>
    public virtual void Tucking()
    {
    }

    /// <summary>The fold is over and the window is about to hide: let go of everything, so nothing runs while tucked.</summary>
    public virtual void Tucked()
    {
    }

    /// <summary>A print joined the row (it is in <see cref="BoardState.Order"/> and on the track).</summary>
    public virtual void Added(BoardItem item, bool animate, Flight? flight)
    {
    }

    /// <summary>A print was unpinned: it is <see cref="BoardItem.Leaving"/> and out of the order, still on the track.</summary>
    public virtual void Removed(BoardItem item, Removal how)
    {
    }

    /// <summary>A print changed; <paramref name="old"/> is how it was.</summary>
    public virtual void Updated(BoardItem item, Print old)
    {
    }

    /// <summary>The app's new row order: every id, in order.</summary>
    public virtual void Reordered(IReadOnlyList<string> ids)
    {
    }

    public virtual void Copied(BoardItem item)
    {
    }

    /// <summary>A print's file is being dragged out (<see cref="BoardState.DraggingId"/> is set); the item is null if it is gone.</summary>
    public virtual void DragStarted(string id, BoardItem? item)
    {
    }

    public virtual void DragEnded(string id, BoardItem? item)
    {
    }

    /// <summary>The window went click-through: the pointer is off the board.</summary>
    public virtual void PointerLeft()
    {
    }

    public virtual void Notice(string text)
    {
    }

    /// <summary>Sound, felt, frame, paper or speed changed (<see cref="BoardContext.Look"/> is the new look).</summary>
    public virtual void LookChanged(BoardLook look)
    {
    }

    /// <summary>Light, dark or high contrast changed (<see cref="BoardContext.Theme"/>).</summary>
    public virtual void ThemeChanged()
    {
    }

    /// <summary>Windows asks for less motion, or no longer does (<see cref="BoardState.Reduced"/>).</summary>
    public virtual void ReducedChanged()
    {
    }

    /// <summary>The window's size or scale changed (debounced): lay out at once, without slides.</summary>
    public virtual void Resized()
    {
    }
}
