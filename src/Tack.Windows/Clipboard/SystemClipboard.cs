using Windows.Win32;
using Windows.Win32.System.Ole;

namespace Tack.Windows.Clipboard;

/// <summary>
/// What Windows says about the clipboard without anyone opening it: whether it
/// changed, who wrote last, and whether a picture is on it. Safe on any
/// thread, the UI thread included; reading what is on it is
/// <see cref="ClipboardView"/>'s.
/// </summary>
static class SystemClipboard
{
    static readonly uint s_png = PInvoke.RegisterClipboardFormat(ClipboardFormats.Png);

    /// <summary>Bumped by Windows on every clipboard change.</summary>
    public static uint Sequence() => PInvoke.GetClipboardSequenceNumber();

    /// <summary>The window that last wrote to the clipboard; 0 when that app wrote without naming one.</summary>
    public static unsafe nint Owner() => (nint)PInvoke.GetClipboardOwner().Value;

    /// <summary>Any bitmap shows up as CF_DIB, which Windows makes from the others.</summary>
    public static bool HasPicture() =>
        PInvoke.IsClipboardFormatAvailable((uint)CLIPBOARD_FORMAT.CF_DIB)
        || (s_png != 0 && PInvoke.IsClipboardFormatAvailable(s_png));
}
