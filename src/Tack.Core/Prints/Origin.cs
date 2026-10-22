namespace Tack.Core.Prints;

/// <summary>Where a print's file came from.</summary>
public enum Origin
{
    /// <summary>A screenshot file in the Screenshots folder, or wherever else the user keeps it.</summary>
    Folder,

    /// <summary>
    /// A file Tack wrote itself: a Snipping Tool image from the clipboard or a
    /// picture dropped on the board, in Captures, or a note's text file, in Notes.
    /// </summary>
    Capture,
}
