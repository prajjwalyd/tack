using System.Runtime.InteropServices;
using Tack.Windows.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using WindowsClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Tack.Windows.Clipboard;

/// <summary>
/// Puts a print on the clipboard, as the user asked: a note's text, or a
/// picture in full (not its thumbnail), through Windows' own clipboard on a
/// <see cref="ClipboardThread"/>, so these are safe to call from the UI
/// thread. Each copy is drawn in full at once, so no app pasting it ever waits
/// on Tack. Each throws <see cref="IOException"/> when the clipboard stays
/// busy or refuses the copy.
/// </summary>
public static class ClipboardWriter
{
    static readonly byte[] s_pngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Puts text on the clipboard, with Windows line breaks.</summary>
    public static Task CopyTextAsync(string text) => PutAsync(package => package.SetText(WindowsLineBreaks(text)));

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
    /// Puts a picture on the clipboard as PNG, which keeps its transparency
    /// exactly, and as a bitmap, which Windows makes from it for every other
    /// app. <paramref name="png"/> is the picture's own PNG file, when it came
    /// from one: it goes on as it is instead of being encoded again.
    /// </summary>
    public static async Task PutPictureAsync(Picture picture, byte[]? png = null)
    {
        png ??= await PictureEncoder.PngAsync(picture).ConfigureAwait(false);
        await PutAsync(package =>
        {
            package.SetData(ClipboardFormats.Png, new MemoryStream(png, writable: false).AsRandomAccessStream());
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(new MemoryStream(png, writable: false).AsRandomAccessStream()));
        }).ConfigureAwait(false);
    }

    /// <summary>Text with every line break as CR LF, as Windows apps expect on the clipboard.</summary>
    internal static string WindowsLineBreaks(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    /// <summary>Puts on the clipboard what <paramref name="fill"/> adds to a package, made on the clipboard thread.</summary>
    static Task PutAsync(Action<DataPackage> fill) => ClipboardThread.RunAsync(() =>
    {
        try
        {
            var package = new DataPackage();
            fill(package);
            return ClipboardThread.Retrying(() =>
            {
                WindowsClipboard.SetContent(package);
                WindowsClipboard.Flush();
                return true;
            });
        }
        catch (COMException e)
        {
            throw new IOException("The clipboard is busy.", e);
        }
    });
}
