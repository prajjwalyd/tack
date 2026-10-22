namespace Tack.Core.Files;

/// <summary>
/// The one spelling of a path that exists: full, without a <c>\\?\</c> prefix,
/// with 8.3 short names expanded (Path.GetFullPath does that) and links
/// followed, so two spellings of one folder compare equal.
/// </summary>
public static class CanonicalPath
{
    /// <summary>The canonical spelling, or null if the path does not exist or cannot be read.</summary>
    public static string? Of(string path)
    {
        try
        {
            var full = Path.GetFullPath(WithoutPrefix(path));
            FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
            if (!info.Exists)
            {
                return null;
            }
            if (info.LinkTarget != null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null || !target.Exists)
                {
                    return null;
                }
                full = Path.GetFullPath(WithoutPrefix(target.FullName));
            }
            return Path.TrimEndingDirectorySeparator(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Both name the same folder: the same text or, failing that, the same
    /// folder once resolved, so a short name, a <c>\\?\</c> prefix or a trailing
    /// separator does not make them look different.
    /// </summary>
    public static bool SameDirectory(string a, string b)
    {
        if (PathRules.Key(a) == PathRules.Key(b))
        {
            return true;
        }
        var (resolvedA, resolvedB) = (Of(a), Of(b));
        return resolvedA != null && resolvedB != null && PathRules.Key(resolvedA) == PathRules.Key(resolvedB);
    }

    static string WithoutPrefix(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
        : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
        : path;
}
