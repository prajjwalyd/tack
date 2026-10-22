namespace Tack.Windows.Tests;

/// <summary>A fresh folder in the temp folder for one test, removed afterwards with whatever is left in it.</summary>
sealed class Scratch : IDisposable
{
    public Scratch(string name)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tack-{name}-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>The path of <paramref name="name"/> in the folder.</summary>
    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Writes a small file named <paramref name="name"/> here and returns its path.</summary>
    public string SmallFile(string name)
    {
        var path = File(name);
        System.IO.File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the temp folder's own clean-up.
        }
    }
}
