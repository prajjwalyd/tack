using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.LibraryLoader;

namespace Tack.Windows.Startup;

/// <summary>
/// Makes Windows load DLLs only from Tack's own folder and System32, never
/// from the current folder or PATH, so a planted DLL is never picked up.
/// Called before anything else runs.
/// </summary>
public static class DllSearch
{
    /// <summary>
    /// The UI framework's resource loader, which it looks for in a way the
    /// restricted search does not cover; without it no page loads. Loaded from
    /// Tack's own folder by full path first, so the later lookups find it.
    /// </summary>
    static readonly string[] s_resourceLoader = ["MRM.dll", "Microsoft.Windows.ApplicationModel.Resources.dll"];

    public static void Secure()
    {
        foreach (var name in s_resourceLoader)
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path))
            {
                NativeLibrary.Load(path);
            }
        }
        PInvoke.SetDefaultDllDirectories(LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    }
}
