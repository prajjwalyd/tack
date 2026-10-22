namespace Tack.Core.Storage;

/// <summary>Whether a print from last session can be put back.</summary>
public enum Restoring
{
    /// <summary>Try to read it. A file that cannot be read now (offline, locked) stays listed for the next start.</summary>
    Try,

    /// <summary>Its file no longer exists: the print is dropped.</summary>
    Gone,

    /// <summary>Not a file Tack pins (board.json was edited by hand): the print is dropped.</summary>
    Invalid,
}
