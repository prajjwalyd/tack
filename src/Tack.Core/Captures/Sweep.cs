using Tack.Core.Diagnostics;
using Tack.Core.Files;

namespace Tack.Core.Captures;

/// <summary>
/// The startup sweep: recycles Tack's own files that no board needs any more,
/// left behind by a failed recycle or a crash before a save. Only run it when
/// board.json was read and fully understood: otherwise every capture would
/// look like a leftover.
/// </summary>
public static class Sweep
{
    /// <summary>
    /// A file this young is never swept, pinned or not. Longer than the history
    /// keeps an unkept print (7 days), so the sweep never judges a file the board
    /// might still show.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(8);

    /// <summary>
    /// The newest modification time a file may have and still be swept: older
    /// than <see cref="Grace"/>, and older than board.json's last save. A file
    /// written after that save may be listed only in a later save that failed.
    /// </summary>
    public static DateTime Cutoff(DateTime nowUtc, DateTime? boardSaved)
    {
        var graced = nowUtc - DateTime.MinValue > Grace ? nowUtc - Grace : DateTime.MinValue;
        return boardSaved is { } saved && saved < graced ? saved : graced;
    }

    /// <summary>Recycles leftover captures: images, and half-written files, that no path in <paramref name="keep"/> names.</summary>
    public static List<string> Captures(string folder, IReadOnlyList<string> keep, DateTime cutoffUtc, IRecycler bin) =>
        Where(folder, keep, cutoffUtc, bin, PathRules.IsImage);

    /// <summary>
    /// The sweep for any folder of Tack's own files: <paramref name="ours"/> says
    /// which files Tack wrote (half-written ones always count). Links and other
    /// reparse points are never touched, and a folder that is one itself is not
    /// swept at all: it may lead anywhere. Returns the files that went.
    /// </summary>
    public static List<string> Where(string folder, IReadOnlyList<string> keep, DateTime cutoffUtc, IRecycler bin, Func<string, bool> ours)
    {
        var swept = new List<string>();
        try
        {
            var info = new DirectoryInfo(folder);
            if (!info.Exists)
            {
                return swept;
            }
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || CanonicalPath.Of(folder) is not { } home)
            {
                DevTrace.Write($"{folder} is not a plain folder; nothing in it is swept");
                return swept;
            }
            var kept = KeptNames(home, keep);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                var file = new FileInfo(path);
                var leftover = file.Exists
                    && !file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    && (ours(path) || ReservedFiles.IsPartial(path))
                    && !kept.Contains(Path.GetFileName(path).ToLowerInvariant())
                    && file.LastWriteTimeUtc < cutoffUtc;
                if (leftover && Recycling.RecycleFile(path, bin))
                {
                    swept.Add(path);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DevTrace.Write($"the sweep of {folder} stopped: {e.Message}");
        }
        return swept;
    }

    /// <summary>
    /// The names, lowercased, of the files in <paramref name="home"/> (resolved)
    /// that <paramref name="keep"/> names. A path counts by its folder, resolved,
    /// so another spelling of the same folder cannot make a pinned file look
    /// unreferenced; when its folder cannot be resolved, its name is kept anyway.
    /// </summary>
    static HashSet<string> KeptNames(string home, IReadOnlyList<string> keep)
    {
        var homeKey = PathRules.Key(home);
        var names = new HashSet<string>();
        foreach (var path in keep)
        {
            // The file itself first, which also turns a short name long.
            var resolved = CanonicalPath.Of(path) ?? path;
            var name = Path.GetFileName(resolved);
            if (name.Length == 0)
            {
                continue;
            }
            var parent = Path.GetDirectoryName(resolved);
            var here = parent is null || CanonicalPath.Of(parent) is not { } folder || PathRules.Key(folder) == homeKey;
            if (here)
            {
                names.Add(name.ToLowerInvariant());
            }
        }
        return names;
    }
}
