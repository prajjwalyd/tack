using Tack.Core.Files;
using Tack.Core.Prints;
using Tack.Core.Settings;

namespace Tack.Core.Storage;

/// <summary>
/// What one save writes: taken under the app's lock (cheap, a few paths) and
/// written outside it by the <see cref="Saver"/>.
/// </summary>
public sealed class Snapshot
{
    Snapshot(IReadOnlyList<StoredPrint> prints, TackSettings settings) => (Prints, Settings) = (prints, settings);

    internal IReadOnlyList<StoredPrint> Prints { get; }

    internal TackSettings Settings { get; }

    /// <summary>
    /// The board's <paramref name="prints"/> in row order, then last session's
    /// prints still <paramref name="pending"/> restore (those not on the board
    /// yet), so a save made before the restore is over never forgets them.
    /// </summary>
    public static Snapshot Of(IReadOnlyList<Print> prints, IReadOnlyList<SavedPrint> pending, TackSettings settings)
    {
        var onBoard = prints.Select(p => PathRules.Key(p.Path)).ToHashSet();
        var board = prints.Select(p => Stored(p.Path, p.Kind, p.Origin, p.PinnedAt, p.KeptAt));
        var waiting = pending
            .Where(p => !onBoard.Contains(PathRules.Key(p.Path)))
            .Select(p => Stored(p.Path, p.Kind, p.Origin, p.PinnedAt, p.KeptAt));
        return new Snapshot([.. board, .. waiting], settings);
    }

    static StoredPrint Stored(string path, Kind kind, Origin origin, long pinnedAt, long? keptAt) => new()
    {
        Path = path,
        Kind = kind == Kind.Note ? "note" : null,
        Capture = origin == Origin.Capture,
        PinnedAt = (ulong)Math.Max(pinnedAt, 0),
        Kept = keptAt.HasValue,
        KeptAt = keptAt is { } kept ? (ulong)Math.Max(kept, 0) : null,
    };
}
