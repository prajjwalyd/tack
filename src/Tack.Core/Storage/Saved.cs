using Tack.Core.Prints;
using Tack.Core.Settings;

namespace Tack.Core.Storage;

/// <summary>A board.json, read back.</summary>
public sealed class Saved
{
    public TackSettings Settings { get; init; } = new();

    /// <summary>Last session's prints, in row order.</summary>
    public List<SavedPrint> Prints { get; init; } = [];

    /// <summary>
    /// Drops the prints that have aged out of the history and hands them back,
    /// so they are never decoded just to be thrown away.
    /// </summary>
    public List<SavedPrint> AgeOut(long nowMs) => History.TakeAged(Prints, p => (p.KeptAt, p.PinnedAt), nowMs);
}
