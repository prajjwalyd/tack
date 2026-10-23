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

    /// <summary>
    /// How long a download is left before <see cref="ClearOld()"/> deletes it:
    /// long enough that an installer still running is never pulled from under it.
    /// </summary>
    internal static readonly TimeSpan KeptFor = TimeSpan.FromDays(1);

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
        Delete(folder);
    }

    /// <summary>
    /// Deletes the downloads of earlier updates: an installer that ran is
    /// left in its folder, since it cannot delete itself. Only Tack's own
    /// folders older than <see cref="KeptFor"/>, never a link. Best effort; slow
    /// on a large %TEMP%, so call it off the UI thread.
    /// </summary>
    public static void ClearOld() => ClearOld(DateTime.UtcNow);

    /// <summary>As <see cref="ClearOld()"/>, as if it were <paramref name="nowUtc"/>.</summary>
    internal static void ClearOld(DateTime nowUtc)
    {
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(Path.GetTempPath(), Prefix + "*").ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"updates: %TEMP% could not be read: {e.Message}");
            return;
        }
        foreach (var folder in folders)
        {
            var info = new DirectoryInfo(folder);
            if (Ours(folder)
                && !info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                && IsOld(info.LastWriteTimeUtc, nowUtc))
            {
                Delete(folder);
            }
        }
    }

    /// <summary>True when a download last written at <paramref name="writtenUtc"/> has been kept long enough.</summary>
    internal static bool IsOld(DateTime writtenUtc, DateTime nowUtc) => nowUtc - writtenUtc > KeptFor;

    /// <summary>A folder <see cref="Fresh"/> made: right under %TEMP%, with Tack's prefix.</summary>
    internal static bool Ours(string folder)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        var temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        return parent is not null
            && string.Equals(Path.GetFullPath(parent), Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(folder).StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"updates: the download could not be deleted: {e.Message}");
        }
    }
}
