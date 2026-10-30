namespace Tack.Core.Pinboard.Input;

/// <summary>A file or folder carried onto the board.</summary>
/// <param name="Name">Its name.</param>
/// <param name="ContentType">Its MIME type, if known.</param>
/// <param name="Size">Its size in bytes; 0 for a folder.</param>
/// <param name="Path">Where it is; null or "" for one with no place on disk (inside a zip, an attachment).</param>
/// <param name="Folder">A folder rather than a file.</param>
public sealed record DroppedFile(string Name, string? ContentType, long Size, string? Path = null, bool Folder = false);
