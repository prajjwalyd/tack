using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Screenshots;

/// <summary>
/// The Screenshots folder, where Win+Shift+S and Print Screen save. Tack has
/// no capture tool of its own: whatever lands here ends up on the board.
/// </summary>
public static unsafe class ScreenshotsFolder
{
    /// <summary>
    /// The Screenshots known folder, created if it does not exist yet. Falls
    /// back to Pictures\Screenshots in the user's profile if Windows will not say.
    /// </summary>
    public static string Find()
    {
        var folder = Known() ?? Fallback(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Watching it will say so.
        }
        return folder;
    }

    static string Fallback(string profile) => Path.Combine(profile, "Pictures", "Screenshots");

    static string? Known()
    {
        var id = PInvoke.FOLDERID_Screenshots;
        PWSTR path;
        var result = PInvoke.SHGetKnownFolderPath(&id, KNOWN_FOLDER_FLAG.KF_FLAG_CREATE, HANDLE.Null, &path);
        try
        {
            var text = result.Succeeded ? path.ToString() : null;
            return string.IsNullOrEmpty(text) ? null : text;
        }
        finally
        {
            // Windows may hand back memory even when it fails.
            PInvoke.CoTaskMemFree(path);
        }
    }
}
