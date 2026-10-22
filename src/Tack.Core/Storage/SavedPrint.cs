using Tack.Core.Prints;

namespace Tack.Core.Storage;

/// <summary>A print from board.json, to pin again.</summary>
/// <param name="Path">The print's file.</param>
/// <param name="Kind">A screenshot or a note.</param>
/// <param name="Origin">Whose file it is: the user's, or one Tack wrote.</param>
/// <param name="PinnedAt">Ms since the Unix epoch.</param>
/// <param name="KeptAt">Ms since the Unix epoch; set for a kept print.</param>
public sealed record SavedPrint(string Path, Kind Kind, Origin Origin, long PinnedAt, long? KeptAt);
