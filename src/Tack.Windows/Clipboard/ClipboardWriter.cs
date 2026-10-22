using System.Runtime.InteropServices;
using Tack.Windows.Imaging;
using Windows.Win32.System.Ole;

namespace Tack.Windows.Clipboard;

/// <summary>
/// Puts a print on the clipboard: a note's text, or a picture in full (not its
/// thumbnail). The clipboard is opened on a thread-pool thread, as another app
/// may hold it for a moment, so these are safe to call from the UI thread. Each
/// throws <see cref="IOException"/> when the clipboard stays busy or refuses
/// the copy.
/// </summary>
public static class ClipboardWriter
{
    static readonly byte[] s_pngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Puts text on the clipboard as Unicode text, with Windows line breaks.</summary>
    public static Task CopyTextAsync(string text) => Task.Run(() => PutText(WindowsLineBreaks(text)));

    /// <summary>
    /// Puts an image file's picture on the clipboard. A PNG file goes on as it
    /// is, not encoded again. Throws <see cref="IOException"/> as well for a file
    /// that cannot be read or decoded.
    /// </summary>
    public static async Task CopyImageAsync(string path)
    {
        var file = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        Picture picture;
        try
        {
            picture = await PictureDecoder.DecodeAsync(file).ConfigureAwait(false);
        }
        catch (InvalidDataException e)
        {
            throw new IOException("The picture could not be decoded.", e);
        }
        await PutPictureAsync(picture, file.AsSpan().StartsWith(s_pngSignature) ? file : null).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts a picture on the clipboard as PNG and as a bitmap, both made before
    /// it is opened. <paramref name="png"/> is the picture's own PNG file, when
    /// it came from one: it goes on as it is instead of being encoded again.
    /// </summary>
    public static async Task PutPictureAsync(Picture picture, byte[]? png = null)
    {
        png ??= await PictureEncoder.PngAsync(picture).ConfigureAwait(false);
        var dib = DibV5.Of(picture);
        await Task.Run(() =>
        {
            using var open = Open();
            open.Empty();
            // PNG first: apps that take the first picture format they know get
            // the one that keeps transparency exactly.
            open.Set(ClipboardFormats.Registered(ClipboardFormats.Png), png);
            open.Set((uint)CLIPBOARD_FORMAT.CF_DIBV5, dib);
        }).ConfigureAwait(false);
    }

    /// <summary>Text with every line break as CR LF, as Windows apps expect on the clipboard.</summary>
    internal static string WindowsLineBreaks(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    /// <summary>Puts text on the clipboard as it is, as Unicode text. Blocks briefly.</summary>
    static void PutText(string text)
    {
        using var open = Open();
        open.Empty();
        var bytes = new byte[(text.Length + 1) * 2];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        open.Set((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT, bytes);
    }

    /// <summary>The clipboard, opened for no window in particular, as copying a print needs none.</summary>
    static ClipboardSession Open() => ClipboardSession.Open(0) ?? throw new IOException("The clipboard is busy.");
}
