using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using static Tack.Windows.Clipboard.ClipboardThread;
using WindowsClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Tack.Windows.Clipboard;

/// <summary>
/// The user's clipboard as it was, to put back after Tack had an app copy its
/// selection: every format Windows will hand over, but not live OLE objects,
/// which only make sense with the app that made them (an Office object copied
/// as such comes back as its text, HTML, RTF and picture). It is put back the
/// way Windows offers for this, kept out of clipboard history and off the
/// user's other devices, which already have the original. Taken and put back
/// on one <see cref="ClipboardThread"/>, as what Windows hands over belongs to
/// the thread that asked for it.
/// </summary>
sealed class SavedClipboard
{
    readonly DataPackage _package = new();
    int _kept;

    SavedClipboard()
    {
    }

    /// <summary>Formats Windows could not hand over (an app that failed to draw one): the clipboard comes back without them.</summary>
    public int Unread { get; private set; }

    /// <summary>Takes what is on the clipboard now. Throws <see cref="COMException"/> if it stays busy.</summary>
    public static SavedClipboard Take()
    {
        var saved = new SavedClipboard();
        var view = Retrying(WindowsClipboard.GetContent);
        foreach (var format in view.AvailableFormats)
        {
            if (ClipboardFormats.IsLiveObject(format))
            {
                continue;
            }
            try
            {
                saved._package.SetData(format, Wait(view.GetDataAsync(format)));
                saved._kept++;
            }
            catch (Exception)
            {
                // Its app could not draw it, or Windows would not hand it over.
                saved.Unread++;
            }
        }
        return saved;
    }

    /// <summary>
    /// Puts it back, drawn in full, unless the clipboard changed after
    /// <paramref name="since"/> (a <see cref="SystemClipboard.Sequence"/>
    /// number): then another app's copy stays, and this is false.
    /// </summary>
    public bool PutBack(uint since)
    {
        // Looked at as late as can be, so another app's copy is not written over.
        if (SystemClipboard.Sequence() != since)
        {
            return false;
        }
        return Retrying(() =>
        {
            if (_kept == 0)
            {
                WindowsClipboard.Clear();
                return true;
            }
            var options = new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
            if (!WindowsClipboard.SetContentWithOptions(_package, options))
            {
                throw new IOException("Windows did not take the clipboard back.");
            }
            WindowsClipboard.Flush();
            return true;
        });
    }
}
