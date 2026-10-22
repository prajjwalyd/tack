namespace Tack.App.Pinboard.Contract;

/// <summary>How an unpinned print leaves the board.</summary>
enum Removal
{
    /// <summary>The pin pops out and the print tumbles into the overhang.</summary>
    Fall,

    /// <summary>A short fade: its file was deleted or moved, or it aged out unseen.</summary>
    Quiet,
}
