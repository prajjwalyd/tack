namespace Tack.Core.Tests;

/// <summary>A fresh folder in the temp folder for one test, removed afterwards.</summary>
sealed class Scratch : IDisposable
{
    public Scratch(string name)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tack-{name}-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>A path inside the folder, its parent folders created.</summary>
    public string File(string relative, string? contents = null)
    {
        var path = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (contents != null)
        {
            System.IO.File.WriteAllText(path, contents);
        }
        return path;
    }

    public string Folder(string relative) => Directory.CreateDirectory(System.IO.Path.Combine(Path, relative)).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own clean-up.
        }
    }
}
