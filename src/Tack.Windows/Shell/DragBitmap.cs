using System.Drawing;
using Tack.Windows.Imaging;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32;

namespace Tack.Windows.Shell;

/// <summary>Puts a picture under the pointer for a shell drag.</summary>
internal static unsafe class DragBitmap
{
    /// <summary>CLR_NONE: no colour key; the alpha channel says what is see-through.</summary>
    const uint NoColorKey = 0xFFFF_FFFF;

    /// <summary>
    /// Attaches <paramref name="image"/> to the drag's data object, held by its
    /// centre. Without it the shell shows a generic icon, which is never worth
    /// failing a drag over, so this only reports whether it worked.
    /// </summary>
    public static bool TryAttach(IDataObject* data, DragImage image)
    {
        if (!image.IsWhole)
        {
            return false;
        }
        if (PInvoke.CoCreateInstance(PInvoke.CLSID_DragDropHelper, null, CLSCTX.CLSCTX_INPROC_SERVER, out IDragSourceHelper* helper).Failed)
        {
            return false;
        }
        try
        {
            var bitmap = Create(image);
            if (bitmap.IsNull)
            {
                return false;
            }
            var drag = new SHDRAGIMAGE
            {
                sizeDragImage = new SIZE(image.Width, image.Height),
                ptOffset = new Point(image.Width / 2, image.Height / 2),
                hbmpDragImage = bitmap,
                crColorKey = new COLORREF(NoColorKey),
            };
            try
            {
                helper->InitializeFromBitmap(drag, data);
                return true;
            }
            catch (Exception)
            {
                // On success the helper owns the bitmap; on failure it is still ours.
                PInvoke.DeleteObject((HGDIOBJ)bitmap.Value);
                return false;
            }
        }
        finally
        {
            helper->Release();
        }
    }

    /// <summary>A 32-bit bitmap holding the image's pixels, or a null handle.</summary>
    static HBITMAP Create(DragImage image)
    {
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = image.Width,
                // Negative: rows run top-down.
                biHeight = -image.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };
        void* bits;
        var bitmap = PInvoke.CreateDIBSection(HDC.Null, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        if (bitmap.IsNull || bits == null)
        {
            if (!bitmap.IsNull)
            {
                PInvoke.DeleteObject((HGDIOBJ)bitmap.Value);
            }
            return HBITMAP.Null;
        }
        image.Bgra.CopyTo(new Span<byte>(bits, image.Bgra.Length));
        return bitmap;
    }
}
