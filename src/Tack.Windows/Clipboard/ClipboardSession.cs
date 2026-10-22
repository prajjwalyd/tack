using System.ComponentModel;
using System.Runtime.InteropServices;
using Tack.Windows.Imaging;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using Windows.Win32;

namespace Tack.Windows.Clipboard;

/// <summary>
/// The clipboard, open; closed again when disposed. While Tack holds it open
/// no other app can write to it (Snipping Tool's next copy would fail), so it
/// is opened only to copy bytes, never across a wait, and closed on the
/// thread that opened it.
/// </summary>
sealed unsafe class ClipboardSession : IDisposable
{
    /// <summary>Another app may hold the clipboard open for a moment.</summary>
    const int OpenTries = 12;
    static readonly TimeSpan s_openGap = TimeSpan.FromMilliseconds(15);

    ClipboardSession()
    {
    }

    /// <summary>
    /// Opens the clipboard, retrying for a moment while another app has it
    /// (so it blocks briefly: never call it on the UI thread). Opening does not
    /// make <paramref name="owner"/> the owner (emptying it does); it tells
    /// anyone looking into a busy clipboard who holds it open.
    /// </summary>
    public static ClipboardSession? Open(nint owner)
    {
        for (var attempt = 0; attempt < OpenTries; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(s_openGap);
            }
            if (PInvoke.OpenClipboard((HWND)owner))
            {
                return new ClipboardSession();
            }
        }
        return null;
    }

    /// <summary>Whether a format is on the clipboard; works open or closed.</summary>
    public static bool Available(uint format) => format != 0 && PInvoke.IsClipboardFormatAvailable(format);

    /// <summary>Every format on the clipboard, in the order its owner offered them.</summary>
    public IEnumerable<uint> Formats()
    {
        for (var format = PInvoke.EnumClipboardFormats(0); format != 0; format = PInvoke.EnumClipboardFormats(format))
        {
            yield return format;
        }
    }

    /// <summary>The bytes of a memory format, or null if it is missing, empty or not memory.</summary>
    public byte[]? Bytes(uint format)
    {
        var global = (HGLOBAL)(void*)PInvoke.GetClipboardData(format).Value;
        if (global.IsNull)
        {
            return null;
        }
        var size = PInvoke.GlobalSize(global);
        if (size == 0 || size > int.MaxValue)
        {
            return null;
        }
        var memory = PInvoke.GlobalLock(global);
        if (memory == null)
        {
            return null;
        }
        try
        {
            return new ReadOnlySpan<byte>(memory, (int)size).ToArray();
        }
        finally
        {
            PInvoke.GlobalUnlock(global);
        }
    }

    /// <summary>Empties the clipboard, making the window it was opened for the owner.</summary>
    public void Empty()
    {
        if (!PInvoke.EmptyClipboard())
        {
            throw new IOException("The clipboard could not be emptied.", new Win32Exception());
        }
    }

    /// <summary>Sets one memory format; once set, the memory belongs to the clipboard.</summary>
    public void Set(uint format, ReadOnlySpan<byte> bytes)
    {
        var global = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)Math.Max(bytes.Length, 1));
        if (global.IsNull)
        {
            throw new IOException("No memory for the clipboard.", new Win32Exception());
        }
        var memory = PInvoke.GlobalLock(global);
        if (memory == null)
        {
            PInvoke.GlobalFree(global);
            throw new IOException("Clipboard memory could not be locked.", new Win32Exception());
        }
        bytes.CopyTo(new Span<byte>(memory, bytes.Length));
        PInvoke.GlobalUnlock(global);
        if (PInvoke.SetClipboardData(format, (HANDLE)global.Value).IsNull)
        {
            var error = new Win32Exception();
            PInvoke.GlobalFree(global);
            throw new IOException($"The clipboard refused format {format}.", error);
        }
    }

    /// <summary>CF_UNICODETEXT up to its first null, or null if there is none.</summary>
    public string? Text()
    {
        var bytes = Bytes((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT);
        if (bytes == null)
        {
            return null;
        }
        var units = MemoryMarshal.Cast<byte, char>(bytes);
        var end = units.IndexOf('\0');
        return (end < 0 ? units : units[..end]).ToString();
    }

    /// <summary>The paths of files copied in Explorer (CF_HDROP), or null if there are none.</summary>
    public List<string>? Files()
    {
        var drop = (HDROP)PInvoke.GetClipboardData((uint)CLIPBOARD_FORMAT.CF_HDROP).Value;
        if (drop.IsNull)
        {
            return null;
        }
        var count = PInvoke.DragQueryFile(drop, uint.MaxValue, default, 0);
        var paths = new List<string>((int)Math.Min(count, 4096));
        for (uint i = 0; i < count; i++)
        {
            var length = PInvoke.DragQueryFile(drop, i, default, 0);
            var buffer = new char[length + 1];
            fixed (char* chars = buffer)
            {
                var got = PInvoke.DragQueryFile(drop, i, chars, (uint)buffer.Length);
                paths.Add(new string(chars, 0, (int)got));
            }
        }
        return paths;
    }

    /// <summary>
    /// The clipboard's picture: its PNG, else the bitmap Windows makes from any
    /// other bitmap format. Only one is asked for, as its app may have to draw
    /// each one on demand. CF_DIB rather than CF_DIBV5: apps disagree on where a
    /// V5 header's colour masks go.
    /// </summary>
    public CopiedPicture? Picture()
    {
        var png = ClipboardFormats.Registered(ClipboardFormats.Png);
        if (Available(png) && Bytes(png) is { } file)
        {
            return new CopiedPicture(CopiedFormat.Png, file);
        }
        return Bytes((uint)CLIPBOARD_FORMAT.CF_DIB) is { } dib ? new CopiedPicture(CopiedFormat.Dib, dib) : null;
    }

    public void Dispose() => PInvoke.CloseClipboard();
}
