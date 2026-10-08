//! Native file drag for a print, so it can be dropped wherever Windows
//! accepts a file. The data object is the shell's own for the file, so
//! targets get what an Explorer drag would give them, and the target picks
//! copy or move. Explorer can finish a move after `SHDoDragDrop` returns, so
//! the caller re-checks the file for a moment afterwards.

use std::path::Path;

use tack_core::thumbnail::DragImage;
use windows::core::{IUnknown, Result, HSTRING};
use windows::Win32::Foundation::{COLORREF, E_FAIL, HWND, POINT, SIZE};
use windows::Win32::Graphics::Gdi::{
    CreateDIBSection, DeleteObject, BITMAPINFO, BITMAPINFOHEADER, BI_RGB, DIB_RGB_COLORS, HGDIOBJ,
};
use windows::Win32::System::Com::{CoCreateInstance, IBindCtx, IDataObject, CLSCTX_INPROC_SERVER};
use windows::Win32::System::Ole::{IDropSource, OleInitialize, DROPEFFECT, DROPEFFECT_COPY, DROPEFFECT_MOVE};
use windows::Win32::UI::Shell::{
    BHID_DataObject, CLSID_DragDropHelper, IDragSourceHelper, IShellItem, SHCreateItemFromParsingName, SHDoDragDrop,
    SHDRAGIMAGE,
};

/// Runs the OLE drag loop for one file, with `image` under the pointer.
/// Modal: returns after the drop (or a cancelled drag). Must be called on
/// the UI thread, with the left button still down.
pub fn drag_file(hwnd: Option<HWND>, path: &Path, image: Option<DragImage>) -> Result<DROPEFFECT> {
    unsafe {
        // Already initialised by the event loop on this thread; that is fine.
        let _ = OleInitialize(None);
        let item: IShellItem = SHCreateItemFromParsingName(&HSTRING::from(path.as_os_str()), None::<&IBindCtx>)?;
        let data: IDataObject = item.BindToHandler(None::<&IBindCtx>, &BHID_DataObject)?;
        if let Some(image) = image {
            // Without it the shell shows a generic icon; never worth failing over.
            let _ = set_drag_image(&data, &image);
        }
        SHDoDragDrop(hwnd, &data, None::<&IDropSource>, DROPEFFECT_COPY | DROPEFFECT_MOVE)
    }
}

/// The print itself follows the pointer, held by its centre.
unsafe fn set_drag_image(data: &IDataObject, image: &DragImage) -> Result<()> {
    let helper: IDragSourceHelper = CoCreateInstance(&CLSID_DragDropHelper, None::<&IUnknown>, CLSCTX_INPROC_SERVER)?;
    let info = BITMAPINFO {
        bmiHeader: BITMAPINFOHEADER {
            biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
            biWidth: image.width,
            // Negative: rows run top-down.
            biHeight: -image.height,
            biPlanes: 1,
            biBitCount: 32,
            biCompression: BI_RGB.0,
            ..Default::default()
        },
        ..Default::default()
    };
    let mut bits = std::ptr::null_mut();
    let bitmap = CreateDIBSection(None, &info, DIB_RGB_COLORS, &mut bits, None, 0)?;
    if bits.is_null() {
        let _ = DeleteObject(HGDIOBJ(bitmap.0));
        return Err(windows::core::Error::from(E_FAIL));
    }
    std::ptr::copy_nonoverlapping(image.bgra.as_ptr(), bits as *mut u8, image.bgra.len());
    let shdi = SHDRAGIMAGE {
        sizeDragImage: SIZE { cx: image.width, cy: image.height },
        ptOffset: POINT { x: image.width / 2, y: image.height / 2 },
        hbmpDragImage: bitmap,
        // CLR_NONE: the alpha channel says what is see-through.
        crColorKey: COLORREF(0xFFFF_FFFF),
    };
    // On success the helper owns the bitmap; on failure it is still ours.
    let result = helper.InitializeFromBitmap(&shdi, data);
    if result.is_err() {
        let _ = DeleteObject(HGDIOBJ(bitmap.0));
    }
    result
}
