using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Shell;

/// <summary>
/// Makes an icon from a PNG file, at the PNG's own size. Windows reads PNG
/// icon images itself (an .ico may hold them since Windows Vista), so the file's
/// bytes go to it as they are: no decoding, no bitmap to build.
/// </summary>
static unsafe class PngIcon
{
    /// <summary>The icon format version CreateIconFromResourceEx asks for.</summary>
    const uint IconVersion = 0x0003_0000;

    /// <summary>The largest file taken: Tack's tray glyphs are a few kilobytes.</summary>
    const int MaxBytes = 1 << 20;

    /// <summary>
    /// The icon, which the caller destroys with DestroyIcon, or a null handle
    /// if the file is missing, too large or not a picture Windows takes.
    /// </summary>
    public static HICON Load(string file)
    {
        byte[] bytes;
        try
        {
            if (new FileInfo(file).Length > MaxBytes)
            {
                return HICON.Null;
            }
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return HICON.Null;
        }
        if (bytes.Length == 0)
        {
            return HICON.Null;
        }
        fixed (byte* data = bytes)
        {
            // A size of 0 by 0 keeps the picture's own size: it was drawn for the tray at this scale.
            return PInvoke.CreateIconFromResourceEx(data, (uint)bytes.Length, true, IconVersion, 0, 0, IMAGE_FLAGS.LR_DEFAULTCOLOR);
        }
    }
}
