namespace Tack.Core.Files;

/// <summary>Which copied files pinning the selection pins where they are, like screenshot files.</summary>
public static class PinnableFiles
{
    /// <summary>The PNG and JPEG files among <paramref name="paths"/>, on a drive letter; others are not pinned (yet).</summary>
    public static List<string> Among(IEnumerable<string> paths) =>
        [.. paths.Where(p => PathRules.IsOnDrive(p) && PathRules.IsImage(p) && File.Exists(p))];
}
