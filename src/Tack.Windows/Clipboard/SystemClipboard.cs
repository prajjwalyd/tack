using Windows.Win32;
using Windows.Win32.System.Ole;
using static Tack.Windows.Clipboard.ClipboardContent;

namespace Tack.Windows.Clipboard;

/// <summary>
/// The clipboard as Tack reads it: who wrote last, whether a picture is on it,
/// and what an app just copied. Reading is only ever done right after the
/// user pinned a selection or Snipping Tool copied a capture. The quick
/// checks suit any thread; reading opens the clipboard, so it never runs on
/// the UI thread.
/// </summary>
static class SystemClipboard
{
    /// <summary>A copied picture is read again if its app failed to draw it on demand.</summary>
    const int PictureTries = 4;
    static readonly TimeSpan s_pictureGap = TimeSpan.FromMilliseconds(60);

    /// <summary>Bumped by Windows on every clipboard change.</summary>
    public static uint Sequence() => PInvoke.GetClipboardSequenceNumber();

    /// <summary>The window that last wrote to the clipboard; 0 when that app wrote without naming one.</summary>
    public static unsafe nint Owner() => (nint)PInvoke.GetClipboardOwner().Value;

    /// <summary>Whether the registered format <paramref name="name"/> is on the clipboard.</summary>
    public static bool HasFormat(string name) => ClipboardSession.Available(ClipboardFormats.Registered(name));

    /// <summary>Any bitmap shows up as CF_DIB, which Windows makes from the others.</summary>
    public static bool HasPicture() =>
        ClipboardSession.Available((uint)CLIPBOARD_FORMAT.CF_DIB) || HasFormat(ClipboardFormats.Png);

    /// <summary>
    /// What is on the clipboard now: files, else text, else a picture. Text that
    /// is only whitespace or control characters counts as none, so a copied
    /// picture that also offers a placeholder character is a picture. A picture
    /// is decoded with the clipboard closed.
    /// </summary>
    public static async Task<ClipboardContent> ReadAsync(nint owner)
    {
        for (var attempt = 0; attempt < PictureTries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(s_pictureGap).ConfigureAwait(false);
            }
            var (content, picture) = ReadOnce(owner);
            if (content != null)
            {
                return content;
            }
            if (picture != null && await picture.DecodeAsync().ConfigureAwait(false) is { } decoded)
            {
                return new PictureContent(decoded);
            }
        }
        return Empty;
    }

    /// <summary>One look: the content, or the picture to decode, or neither to look again.</summary>
    static (ClipboardContent? Content, CopiedPicture? Picture) ReadOnce(nint owner)
    {
        using var open = ClipboardSession.Open(owner);
        if (open == null)
        {
            return (Empty, null);
        }
        if (PrivacyMarkers.IsPrivate(PrivacyMarkers.Read(open)))
        {
            return (Private, null);
        }
        if (open.Files() is { Count: > 0 } files)
        {
            return (new FilesContent(files), null);
        }
        if (open.Text() is { } text && text.Any(c => !char.IsWhiteSpace(c) && !char.IsControl(c)))
        {
            return (new TextContent(text), null);
        }
        return HasPicture() ? (null, open.Picture()) : (Empty, null);
    }

    /// <summary>
    /// One try at copying the picture off the clipboard, if it is still the copy
    /// heard at <paramref name="since"/> (a <see cref="Sequence"/> number) and
    /// not marked private, all in one opening so nothing can change in between.
    /// <paramref name="window"/> is named as the one holding it open. The reason
    /// is meaningful only when there is no picture.
    /// </summary>
    public static (CopiedPicture? Picture, Untaken Why) TakePicture(nint window, uint since)
    {
        // Looked at before opening too, so a stale copy keeps nobody waiting.
        if (Sequence() != since)
        {
            return (null, Untaken.Replaced);
        }
        using var open = ClipboardSession.Open(window);
        if (open == null)
        {
            return (null, Untaken.Busy);
        }
        if (Sequence() != since)
        {
            return (null, Untaken.Replaced);
        }
        if (PrivacyMarkers.IsPrivate(PrivacyMarkers.Read(open)))
        {
            return (null, Untaken.Private);
        }
        return open.Picture() is { } picture ? (picture, Untaken.Busy) : (null, Untaken.Unreadable);
    }
}
