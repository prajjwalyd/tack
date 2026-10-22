namespace Tack.Core.Screenshots;

/// <summary>What the app does about one change in the Screenshots folder.</summary>
public enum FileStep
{
    /// <summary>Nothing: not a screenshot, not on the board, or already in hand.</summary>
    Ignore,

    /// <summary>A new screenshot: decode it, patiently, pin it, then call <see cref="FolderEvents.Finished"/>.</summary>
    Pin,

    /// <summary>
    /// A pinned file was written to: wait <see cref="FolderEvents.UpdateSettle"/>,
    /// call <see cref="FolderEvents.Finished"/>, then redraw its print.
    /// </summary>
    Redraw,

    /// <summary>
    /// A pinned file went: after <see cref="FolderEvents.RemoveGrace"/>, redraw
    /// its print if the file is back, else unpin it.
    /// </summary>
    CheckGone,
}
