using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using WinRT;
using static Tack.Windows.Clipboard.ClipboardContent;
using static Tack.Windows.Clipboard.ClipboardThread;
using WindowsClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Tack.Windows.Clipboard;

/// <summary>
/// The clipboard, read through Windows' own clipboard rather than by opening
/// it. Windows fetches each format, straight from the app that copied it
/// while that app still holds it, and leaves the clipboard free for every
/// other app meanwhile, however long that app takes to draw a picture. (Tack
/// opening the clipboard itself held it shut all that time, so Snipping
/// Tool's next copy failed and clipboard history stalled.) Everything here
/// runs on a <see cref="ClipboardThread"/>, never on the UI thread.
/// </summary>
static class ClipboardView
{
    /// <summary>
    /// What is on the clipboard now: files, else text, else a picture. Text
    /// that is only whitespace or control characters counts as none, so a
    /// copied picture that also offers a placeholder character is a picture.
    /// On the clipboard thread; <see cref="ClipboardContent.Empty"/> if the
    /// clipboard stays busy.
    /// </summary>
    public static ClipboardContent Read()
    {
        try
        {
            return Retrying(() => ReadOnce(WindowsClipboard.GetContent()));
        }
        catch (COMException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// One try at taking the capture heard at <paramref name="since"/> (a
    /// <see cref="SystemClipboard.Sequence"/> number): not if it was marked
    /// private, and only if it is still that very copy once read. The reason
    /// is meaningful only when there is no picture.
    /// </summary>
    public static Task<(CopiedPicture? Picture, Untaken Why)> TakePictureAsync(uint since) => RunAsync(() =>
    {
        // Looked at first, so a stale copy is never fetched.
        if (SystemClipboard.Sequence() != since)
        {
            return (null, Untaken.Replaced);
        }
        try
        {
            var view = WindowsClipboard.GetContent();
            if (PrivacyMarkers.IsPrivate(view))
            {
                return (null, Untaken.Private);
            }
            var picture = Picture(view);
            if (SystemClipboard.Sequence() != since)
            {
                return (null, Untaken.Replaced);
            }
            return picture != null ? (picture, Untaken.Busy) : (null, Untaken.Unreadable);
        }
        catch (COMException)
        {
            // Another app had the clipboard open, or the copying app failed to draw its picture.
            return ((CopiedPicture?)null, Untaken.Busy);
        }
    });

    /// <summary>The bytes of a stream Windows handed over.</summary>
    public static byte[] Bytes(IRandomAccessStream stream)
    {
        using var read = stream.AsStreamForRead();
        using var memory = new MemoryStream();
        read.CopyTo(memory);
        return memory.ToArray();
    }

    static ClipboardContent ReadOnce(DataPackageView view)
    {
        if (PrivacyMarkers.IsPrivate(view))
        {
            return Private;
        }
        if (view.Contains(StandardDataFormats.StorageItems) && Wait(view.GetStorageItemsAsync()) is { Count: > 0 } items)
        {
            return new FilesContent([.. items.Select(item => item.Path)]);
        }
        if (view.Contains(StandardDataFormats.Text) && Wait(view.GetTextAsync()) is { } text && HasContent(text))
        {
            return new TextContent(text);
        }
        return Picture(view)?.DecodeAsync().GetAwaiter().GetResult() is { } picture ? new PictureContent(picture) : Empty;
    }

    /// <summary>Whether text has something in it besides whitespace, control characters and object placeholders.</summary>
    public static bool HasContent(string text) =>
        text.Any(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c != '￼');

    /// <summary>
    /// The clipboard's picture: its PNG, else the bitmap Windows makes from any
    /// other bitmap format, which it hands over as a .bmp file. Only one is
    /// asked for, as its app may have to draw each one on demand.
    /// </summary>
    static CopiedPicture? Picture(DataPackageView view)
    {
        if (view.Contains(ClipboardFormats.Png))
        {
            return new CopiedPicture(CopiedFormat.Png, Bytes(Wait(view.GetDataAsync(ClipboardFormats.Png)).As<IRandomAccessStream>()));
        }
        if (view.Contains(StandardDataFormats.Bitmap))
        {
            using var stream = Wait(Wait(view.GetBitmapAsync()).OpenReadAsync());
            return new CopiedPicture(CopiedFormat.Bitmap, Bytes(stream));
        }
        return null;
    }
}
