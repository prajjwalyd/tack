namespace Tack.Windows.Clipboard;

/// <summary>How putting a snapshot back went, when nothing failed.</summary>
enum Restored
{
    /// <summary>The snapshot is back on the clipboard.</summary>
    Done,

    /// <summary>Another app wrote to the clipboard after the snapshot's moment: theirs stays.</summary>
    Superseded,
}
