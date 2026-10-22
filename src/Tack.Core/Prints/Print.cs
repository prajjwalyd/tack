using Tack.Core.Files;
using Tack.Core.Notes;
using Tack.Core.Thumbnails;

namespace Tack.Core.Prints;

/// <summary>
/// One screenshot or note pinned to the board. Tack points at screenshot files
/// where they already are and leaves them there; captures and notes are files
/// Tack wrote itself, so nobody else knows they are there and Tack cleans them up.
/// </summary>
public sealed record Print
{
    /// <summary>Opaque and stable for the print's life on the board; never saved.</summary>
    public required string Id { get; init; }

    public required string Path { get; init; }

    /// <summary>The file name, e.g. "Screenshot 2026-10-07 231455.png".</summary>
    public string Name { get; init; } = string.Empty;

    public Kind Kind { get; init; }

    /// <summary>The picture's thumbnail; null for a note.</summary>
    public Thumb? Thumb { get; init; }

    /// <summary>When it was first pinned, ms since the Unix epoch. Survives restarts; the history ages prints by it.</summary>
    public long PinnedAt { get; init; }

    /// <summary>When it was kept, ms since the Unix epoch; null if it is not kept.</summary>
    public long? KeptAt { get; init; }

    /// <summary>A note's text and link; null for a screenshot.</summary>
    public NoteBody? Note { get; init; }

    /// <summary>The file's stamp when its thumbnail was made, so an edit can be told from a stray event.</summary>
    public FileStamp? Stamp { get; init; }

    public Origin Origin { get; init; }

    /// <summary>When a live arrival reached the board, on the monotonic clock; null for a restored print.</summary>
    public TimeSpan? Arrived { get; init; }

    /// <summary>Kept prints lead the row and never age out.</summary>
    public bool Kept => KeptAt.HasValue;

    /// <summary>The original picture's size in pixels; 0 for a note.</summary>
    public int Width => Thumb?.Width ?? 0;

    public int Height => Thumb?.Height ?? 0;

    public ulong? Hash => Thumb?.Hash;

    public bool IsNote => Kind == Kind.Note;

    /// <summary>Tack wrote this print's file, so Tack recycles it when the print leaves the board.</summary>
    public bool Owned => Origin == Origin.Capture;
}
