using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Shell;

/// <summary>The shell's own object for a file, which its drag and file operations work on.</summary>
internal static unsafe class ShellItem
{
    /// <summary>The item for the file at <paramref name="path"/>. Throws if there is none; the caller releases it.</summary>
    public static IShellItem* FromPath(string path)
    {
        PInvoke.SHCreateItemFromParsingName(Path.GetFullPath(path), null, out IShellItem* item).ThrowOnFailure();
        return item;
    }
}
