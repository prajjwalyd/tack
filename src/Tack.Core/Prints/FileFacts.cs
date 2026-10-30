namespace Tack.Core.Prints;

/// <summary>What a file print says about its file, read when it was pinned.</summary>
/// <param name="Folder">A folder rather than a file.</param>
/// <param name="Size">The file's size in bytes; 0 for a folder.</param>
/// <param name="Type">The kind of file in Windows' own words, e.g. "Microsoft Word Document"; "" if unknown.</param>
public sealed record FileFacts(bool Folder, long Size, string Type);
