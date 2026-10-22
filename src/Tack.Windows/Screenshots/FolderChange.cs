namespace Tack.Windows.Screenshots;

/// <summary>What happened to a file in a watched folder.</summary>
public enum FolderChange
{
    /// <summary>Created, or renamed or moved into the folder.</summary>
    Arrived,

    /// <summary>
    /// Deleted, or renamed or moved away. Editors often save by deleting and
    /// renaming a temp file into place, so the file may be back a moment later.
    /// </summary>
    Gone,

    /// <summary>Written to.</summary>
    Changed,
}
