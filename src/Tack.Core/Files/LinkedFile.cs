namespace Tack.Core.Files;

/// <summary>A file or folder a print links to where it is, which the user may move or delete at any time.</summary>
public static class LinkedFile
{
    /// <summary>
    /// The file or folder is no longer there, though its drive is: moved or
    /// deleted. One on a drive that is not there now (a USB stick pulled out)
    /// is not gone; it may come back.
    /// </summary>
    public static bool IsGone(string path) =>
        !Path.Exists(path) && Path.GetPathRoot(path) is { Length: > 0 } root && Directory.Exists(root);
}
