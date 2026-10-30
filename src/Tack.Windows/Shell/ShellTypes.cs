using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Shell;

/// <summary>What Explorer calls a kind of file ("Microsoft Word Document", "File folder"), by its name alone. Safe on any thread.</summary>
public static unsafe class ShellTypes
{
    /// <summary>The type of the file or folder at <paramref name="path"/>, read from its extension without touching it; "" if Windows has no word for it.</summary>
    public static string NameOf(string path, bool folder)
    {
        SHFILEINFOW info;
        var attributes = folder ? FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY : FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL;
        nuint found;
        fixed (char* name = path)
        {
            found = PInvoke.SHGetFileInfo(name, attributes, &info, (uint)sizeof(SHFILEINFOW),
                SHGFI_FLAGS.SHGFI_TYPENAME | SHGFI_FLAGS.SHGFI_USEFILEATTRIBUTES);
        }
        return found == 0 ? string.Empty : info.szTypeName.ToString();
    }
}
