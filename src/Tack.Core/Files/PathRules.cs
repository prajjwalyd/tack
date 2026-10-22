namespace Tack.Core.Files;

/// <summary>How Tack compares and checks the paths it handles. Windows paths are case-insensitive.</summary>
public static class PathRules
{
    /// <summary>The path as compared: every path comparison in Tack goes through this.</summary>
    public static string Key(string path) => path.ToLowerInvariant();

    /// <summary>
    /// A path to a file and nothing more: a file name, and no <c>.</c> or <c>..</c>
    /// anywhere that could make it point outside the folder it names.
    /// </summary>
    public static bool IsPlain(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        if (Path.GetFileName(trimmed).Length == 0)
        {
            return false;
        }
        foreach (var part in trimmed.Split('\\', '/'))
        {
            if (part is "." or "..")
            {
                return false;
            }
        }
        return true;
    }

    public static string FileName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    /// <summary>
    /// A full path on a drive letter (<c>C:\...</c>). Anything else is refused
    /// before it is touched: a UNC path would make Windows connect to that
    /// server and offer it the user's sign-in, and <c>\\?\</c> and <c>\\.\</c>
    /// paths reach devices and the same servers.
    /// </summary>
    public static bool IsOnDrive(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';

    /// <summary>The screenshot formats Tack pins.</summary>
    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
}
