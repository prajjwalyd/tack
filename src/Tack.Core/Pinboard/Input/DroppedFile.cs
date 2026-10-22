namespace Tack.Core.Pinboard.Input;

/// <summary>A file carried onto the board: its name, its MIME type if known, and its size in bytes.</summary>
public sealed record DroppedFile(string Name, string? ContentType, long Size);
