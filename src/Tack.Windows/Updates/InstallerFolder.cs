using Tack.Core.Diagnostics;

namespace Tack.Windows.Updates;

/// <summary>
/// Where a downloaded installer waits: a folder of its own under %TEMP%,
/// made fresh for each download, so nothing already there is run or
/// overwritten. A folder Tack did not make here is never deleted.
/// </summary>
public static class InstallerFolder
{
    /// <summary>The start of every such folder's name.</summary>
    internal const string Prefix = "tack-update-";

    /// <summary>Makes a new, empty folder and returns its path.</summary>
    public static string Fresh()
    {
        var folder = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Deletes the folder <paramref name="file"/> was downloaded into, and the
    /// file with it: it failed its check, or will not be run. Best effort.
    /// </summary>
    public static void Discard(string file)
    {
        if (Path.GetDirectoryName(file) is not { } folder || !Ours(folder))
        {
            return;
        }
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"updates: the download could not be deleted: {e.Message}");
        }
    }

    /// <summary>A folder <see cref="Fresh"/> made: right under %TEMP%, with Tack's prefix.</summary>
    internal static bool Ours(string folder)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        var temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        return parent is not null
            && string.Equals(Path.GetFullPath(parent), Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(folder).StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}
