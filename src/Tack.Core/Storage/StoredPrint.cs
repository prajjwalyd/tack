using System.Text.Json.Serialization;

namespace Tack.Core.Storage;

/// <summary>One print in board.json, field for field and in this order.</summary>
internal sealed class StoredPrint
{
    public required string Path { get; set; }

    /// <summary>
    /// "note" for a note; absent for a screenshot. Read as text, so a kind from
    /// a newer Tack skips that print instead of failing the whole file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    public bool Capture { get; set; }

    /// <summary>Ms since the Unix epoch.</summary>
    public ulong? PinnedAt { get; set; }

    public bool Kept { get; set; }

    /// <summary>Ms since the Unix epoch; written as null when the print is not kept.</summary>
    public ulong? KeptAt { get; set; }
}
