namespace Tack.Core.Files;

/// <summary>A file was not moved because its new name was already taken.</summary>
public sealed class FileExistsException(string path, Exception inner)
    : IOException($"{path} already exists.", inner)
{
    public string Path { get; } = path;
}
