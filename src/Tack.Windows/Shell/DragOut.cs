using Tack.Windows.Imaging;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using Windows.Win32;

namespace Tack.Windows.Shell;

/// <summary>
/// Native file drag for a print, so it can be dropped wherever Windows
/// accepts a file. The data object and the allowed effects are the ones an
/// Explorer drag has, so targets treat it like one and pick copy, move or
/// link. Explorer can finish a move after the drag returns, so the caller
/// re-checks the file for a moment afterwards.
/// </summary>
public static unsafe class DragOut
{
    /// <summary>
    /// Chromium pages that set no drop effect take copy only when all three
    /// are offered, as from Explorer; offered copy and move, they move.
    /// </summary>
    const DROPEFFECT Allowed = DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_MOVE | DROPEFFECT.DROPEFFECT_LINK;

    /// <summary>
    /// Runs the shell's drag loop for one file, with <paramref name="image"/>
    /// under the pointer, and returns what the target did. Modal: it returns
    /// once the button is released somewhere. Must be called on the UI thread,
    /// which is single-threaded for COM, with the left button still down.
    /// Throws if the file has no shell item or the drag cannot start.
    /// </summary>
    public static DropEffect DragFile(nint window, string path, DragImage? image)
    {
        // Usually initialised already on the UI thread; this only counts up.
        if (PInvoke.OleInitialize().Failed)
        {
            throw new InvalidOperationException("A drag must start on the UI thread, which is single-threaded for COM.");
        }
        try
        {
            var data = DataObjectOf(path);
            try
            {
                if (image != null)
                {
                    DragBitmap.TryAttach(data, image);
                }
                PInvoke.SHDoDragDrop((HWND)window, data, null, Allowed, out var effect).ThrowOnFailure();
                return (DropEffect)(effect & Allowed);
            }
            finally
            {
                data->Release();
            }
        }
        finally
        {
            PInvoke.OleUninitialize();
        }
    }

    /// <summary>The file as Explorer itself hands it to drop targets. The caller releases it.</summary>
    internal static IDataObject* DataObjectOf(string path)
    {
        var item = ShellItem.FromPath(path);
        try
        {
            item->BindToHandler(null, PInvoke.BHID_DataObject, out IDataObject* data);
            return data;
        }
        finally
        {
            item->Release();
        }
    }
}
