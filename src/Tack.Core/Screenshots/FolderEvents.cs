using Tack.Core.Files;
using Tack.Core.Prints;

namespace Tack.Core.Screenshots;

/// <summary>
/// What a change in the Screenshots folder means for the board: a new image is
/// pinned, an edit to a pinned file redraws its print, and a pinned file that
/// is deleted or moved away is unpinned unless it is back a moment later. It
/// remembers the files in hand, so a burst of events for one file decodes it
/// once. Not thread-safe: use it under the app's lock, with the board.
/// </summary>
public sealed class FolderEvents
{
    /// <summary>How long a fresh file may stay undecodable while it is still being written.</summary>
    public static readonly TimeSpan WritePatience = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Editors often save by deleting and renaming a temp file into place; a
    /// removal is only believed if the file is still gone this much later.
    /// </summary>
    public static readonly TimeSpan RemoveGrace = TimeSpan.FromMilliseconds(500);

    /// <summary>Lets an editor finish writing before the print is redrawn.</summary>
    public static readonly TimeSpan UpdateSettle = TimeSpan.FromMilliseconds(250);

    /// <summary>Files being decoded or waiting to be redrawn, by path key.</summary>
    readonly HashSet<string> _inHand = [];

    /// <summary>A file arrived: saved over a pinned one (a change), or a new screenshot to pin.</summary>
    public FileStep Arrived(Board board, string path)
    {
        if (board.FindPath(path) != null)
        {
            return Changed(board, path);
        }
        if (!PathRules.IsImage(path))
        {
            return FileStep.Ignore;
        }
        return _inHand.Add(PathRules.Key(path)) ? FileStep.Pin : FileStep.Ignore;
    }

    /// <summary>A file was written to: only a pinned one matters, and only once until it is redrawn.</summary>
    public FileStep Changed(Board board, string path) =>
        board.FindPath(path) != null && _inHand.Add(PathRules.Key(path)) ? FileStep.Redraw : FileStep.Ignore;

    /// <summary>A file went: only a pinned one matters.</summary>
    public FileStep Gone(Board board, string path) => board.FindPath(path) != null ? FileStep.CheckGone : FileStep.Ignore;

    /// <summary>The file is no longer in hand: decoded, or its settle is over.</summary>
    public void Finished(string path) => _inHand.Remove(PathRules.Key(path));

    /// <summary>
    /// What redrawing <paramref name="print"/> takes, with its file's stamp
    /// <paramref name="now"/>: reading a note, making a thumbnail, or nothing
    /// (no such print, or a file unchanged since it was drawn).
    /// </summary>
    public static Kind? WhatToRedraw(Print? print, FileStamp? now)
    {
        if (print is null || (print.Stamp is { } old && old == now))
        {
            return null;
        }
        return print.Kind;
    }
}
