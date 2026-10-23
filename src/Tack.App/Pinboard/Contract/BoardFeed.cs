using Microsoft.UI.Dispatching;
using Tack.Core.Diagnostics;
using Tack.Core.Prints;
using Tack.Core.Reveal;

namespace Tack.App.Pinboard.Contract;

/// <summary>
/// What the app tells the board. The app raises each one from any thread; it
/// reaches the board's handlers on the UI thread, through one queue, in the
/// order raised. A raise from the UI thread is queued too, so it never
/// overtakes one raised earlier from elsewhere.
/// </summary>
sealed class BoardFeed(DispatcherQueue ui)
{
    /// <summary>A print was pinned. Animate: a live arrival, pinned on with a sound; else it just appears. A flight: a new capture flies in.</summary>
    public event Action<Print, bool, Flight?>? PrintAdded;

    public event Action<string, Removal>? PrintRemoved;

    /// <summary>The print's picture, size, name, file or kept state changed.</summary>
    public event Action<Print>? PrintUpdated;

    /// <summary>The row's order changed: every pinned print's id, in row order.</summary>
    public event Action<IReadOnlyList<string>>? OrderChanged;

    public event Action<string>? PrintCopied;

    /// <summary>The print's file is being dragged out, until <see cref="PrintDragEnded"/>.</summary>
    public event Action<string>? PrintDragging;

    public event Action<string>? PrintDragEnded;

    /// <summary>The board comes down, the window already shown; with the motion scale as it is now.</summary>
    public event Action<RevealReason, double>? Revealed;

    /// <summary>The board goes up; the window hides once the fold is over.</summary>
    public event Action? Tucking;

    /// <summary>The window went click-through: the pointer left the board.</summary>
    public event Action? PointerLeft;

    /// <summary>A short message under the board, also read out.</summary>
    public event Action<string>? Notice;

    /// <summary>Sound, felt, frame, note paper or speed changed.</summary>
    public event Action<BoardLook>? LookChanged;

    public void RaisePrintAdded(Print print, bool animate, Flight? flight) =>
        Post(() => PrintAdded?.Invoke(print, animate, flight));

    public void RaisePrintRemoved(string id, Removal how) => Post(() => PrintRemoved?.Invoke(id, how));

    public void RaisePrintUpdated(Print print) => Post(() => PrintUpdated?.Invoke(print));

    public void RaiseOrderChanged(IReadOnlyList<string> ids) => Post(() => OrderChanged?.Invoke(ids));

    public void RaisePrintCopied(string id) => Post(() => PrintCopied?.Invoke(id));

    public void RaisePrintDragging(string id) => Post(() => PrintDragging?.Invoke(id));

    public void RaisePrintDragEnded(string id) => Post(() => PrintDragEnded?.Invoke(id));

    public void RaiseRevealed(RevealReason reason, double motionScale) => Post(() => Revealed?.Invoke(reason, motionScale));

    public void RaiseTucking() => Post(() => Tucking?.Invoke());

    public void RaisePointerLeft() => Post(() => PointerLeft?.Invoke());

    public void RaiseNotice(string text) => Post(() => Notice?.Invoke(text));

    public void RaiseLookChanged(BoardLook look) => Post(() => LookChanged?.Invoke(look));

    void Post(DispatcherQueueHandler raise)
    {
        if (!ui.TryEnqueue(raise))
        {
            DevTrace.Write("board feed: the UI thread is gone; an event is dropped");
        }
    }
}
