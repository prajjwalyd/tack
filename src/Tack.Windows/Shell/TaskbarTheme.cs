using Microsoft.Win32;

namespace Tack.Windows.Shell;

/// <summary>
/// The taskbar's mode, light or dark. It is "Choose your default Windows
/// mode" in Settings › Personalization › Colors, which can differ from the
/// apps' mode; the tray icon sits on the taskbar, so it follows this one.
/// </summary>
static class TaskbarTheme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// True when the taskbar is light (SystemUsesLightTheme is not 0). A
    /// missing value means the dark taskbar older Windows always had, as Tack
    /// 0.2 read it.
    /// </summary>
    public static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
