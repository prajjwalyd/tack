namespace Tack.Core.Phone;

/// <summary>A device the user allowed to use the board. Every field is required in board.json.</summary>
public sealed record Device
{
    /// <summary>Its NetBird WireGuard public key: how a device is known, never by address or name.</summary>
    public required string Key { get; init; }

    /// <summary>Its NetBird name when it was allowed ("pixel"), for the list.</summary>
    public required string Name { get; init; }

    /// <summary>When it was allowed, ms since the Unix epoch.</summary>
    public required ulong ApprovedAt { get; init; }
}
