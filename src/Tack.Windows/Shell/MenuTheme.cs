using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Tack.Windows.Shell;

/// <summary>
/// Native menus drawn in the person's app mode, dark or light, as File
/// Explorer's and Tack 0.2's are. Windows draws a process's popup menus light
/// unless the process asks otherwise, through two exports of uxtheme.dll that
/// have only ordinals (SetPreferredAppMode and FlushMenuThemes, there since
/// Windows 10 1903). Checked each time a menu opens, so a change of mode shows
/// on the next menu without anything listening. UI thread only.
/// </summary>
static unsafe class MenuTheme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const int SetPreferredAppModeOrdinal = 135;
    const int FlushMenuThemesOrdinal = 136;

    /// <summary>PreferredAppMode: ForceDark and ForceLight.</summary>
    const int ForceDark = 2;
    const int ForceLight = 3;

    static bool s_loaded;
    static delegate* unmanaged<int, int> s_setPreferredAppMode;
    static delegate* unmanaged<void> s_flushMenuThemes;

    /// <summary>The mode menus were last set to; null before the first menu.</summary>
    static bool? s_dark;

    /// <summary>Sets the menus to the person's app mode, if it changed since the last menu.</summary>
    public static void Match()
    {
        var dark = AppsUseDarkMode();
        if (dark == s_dark || !Load())
        {
            return;
        }
        s_setPreferredAppMode(dark ? ForceDark : ForceLight);
        s_flushMenuThemes();
        s_dark = dark;
    }

    /// <summary>"Choose your default app mode" in Settings › Personalization › Colors: dark when AppsUseLightTheme is 0.</summary>
    static bool AppsUseDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Finds the two exports once; false (menus stay light) if this Windows lacks them.</summary>
    static bool Load()
    {
        if (s_loaded)
        {
            return s_setPreferredAppMode != null && s_flushMenuThemes != null;
        }
        s_loaded = true;
        if (!NativeLibrary.TryLoad("uxtheme.dll", typeof(MenuTheme).Assembly, DllImportSearchPath.System32, out var module))
        {
            return false;
        }
        var uxtheme = new HMODULE(module);
        s_setPreferredAppMode = (delegate* unmanaged<int, int>)(void*)PInvoke.GetProcAddress(uxtheme, new PCSTR((byte*)SetPreferredAppModeOrdinal)).Value;
        s_flushMenuThemes = (delegate* unmanaged<void>)(void*)PInvoke.GetProcAddress(uxtheme, new PCSTR((byte*)FlushMenuThemesOrdinal)).Value;
        return s_setPreferredAppMode != null && s_flushMenuThemes != null;
    }
}
