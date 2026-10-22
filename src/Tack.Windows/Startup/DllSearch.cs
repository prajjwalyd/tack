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
    public static void Secure() =>
        PInvoke.SetDefaultDllDirectories(LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
}
