//! The Windows clipboard, for pinning a selection: reading what an app just
//! copied (text, a picture or files), telling whether it was marked private,
//! and putting back what the user had on the clipboard before.
//!
//! Only [`crate::selection`] reads text from here, and only right after the
//! user pressed the pin shortcut.

use std::path::PathBuf;
use std::time::Duration;

use image::{DynamicImage, RgbaImage};
use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{GlobalFree, HANDLE, HGLOBAL, HWND};
use windows::Win32::System::DataExchange::{
    CloseClipboard, EmptyClipboard, EnumClipboardFormats, GetClipboardData, GetClipboardFormatNameW, GetClipboardOwner,
    GetClipboardSequenceNumber, IsClipboardFormatAvailable, OpenClipboard, RegisterClipboardFormatW, SetClipboardData,
};
use windows::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalSize, GlobalUnlock, GMEM_MOVEABLE};
use windows::Win32::System::Ole::{CF_DIB, CF_HDROP, CF_UNICODETEXT};
use windows::Win32::UI::Shell::{DragQueryFileW, HDROP};

/// The most a snapshot holds; past this the clipboard is not restored.
const SNAPSHOT_LIMIT: usize = 256 * 1024 * 1024;
/// Another app may hold the clipboard open for a moment.
const OPEN_TRIES: u32 = 12;
const OPEN_GAP: Duration = Duration::from_millis(15);

/// Bumped by Windows on every clipboard change.
pub(crate) fn sequence() -> u32 {
    unsafe { GetClipboardSequenceNumber() }
}

/// The window that last wrote to the clipboard; `None` when that app wrote
/// without naming a window.
pub(crate) fn owner() -> Option<HWND> {
    unsafe { GetClipboardOwner() }.ok().filter(|h| !h.is_invalid())
}

/// The clipboard, open; closed again when dropped.
struct Open;

impl Open {
    /// Opens the clipboard for `owner` (`None`: only to read), retrying
    /// while another app has it.
    fn new(owner: Option<HWND>) -> Option<Open> {
        for attempt in 0..OPEN_TRIES {
            if attempt > 0 {
                std::thread::sleep(OPEN_GAP);
            }
            if unsafe { OpenClipboard(owner) }.is_ok() {
                return Some(Open);
            }
        }
        None
    }
}

impl Drop for Open {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseClipboard();
        }
    }
}

/// The bytes of a memory format, while the clipboard is open.
fn bytes_of(format: u32) -> Option<Vec<u8>> {
    unsafe {
        let handle = GetClipboardData(format).ok()?;
        let global = HGLOBAL(handle.0);
        let size = GlobalSize(global);
        if size == 0 {
            return None;
        }
        let ptr = GlobalLock(global) as *const u8;
        if ptr.is_null() {
            return None;
        }
        let bytes = std::slice::from_raw_parts(ptr, size).to_vec();
        let _ = GlobalUnlock(global);
        Some(bytes)
    }
}

fn registered(name: PCWSTR) -> u32 {
    unsafe { RegisterClipboardFormatW(name) }
}

fn format_name(format: u32) -> String {
    let mut buf = [0u16; 128];
    let len = unsafe { GetClipboardFormatNameW(format, &mut buf) }.max(0) as usize;
    String::from_utf16_lossy(&buf[..len])
}

/// Whether a format can be copied as plain bytes and set again later.
fn restorable(format: u32) -> bool {
    match format {
        // GDI handles, or memory holding GDI handles.
        2 /* CF_BITMAP */ | 3 /* CF_METAFILEPICT */ | 9 /* CF_PALETTE */ | 14 /* CF_ENHMETAFILE */ => false,
        0x80 /* CF_OWNERDISPLAY */ | 0x82 | 0x83 | 0x8E /* CF_DSP* GDI forms */ => false,
        // Private and GDI-object ranges: their meaning belongs to the owner.
        0x0200..=0x03FF => false,
        0xC000..=0xFFFF => {
            // Live OLE objects only make sense with the app that made them.
            let name = format_name(format).to_ascii_lowercase();
            !matches!(
                name.as_str(),
                "dataobject"
                    | "ole private data"
                    | "embed source"
                    | "embedded object"
                    | "link source"
                    | "link source descriptor"
                    | "object descriptor"
                    | "ownerlink"
                    | "objectlink"
                    | "native"
            )
        }
        _ => true,
    }
}

/// What was on the clipboard, to put back after pinning a selection.
///
/// Holds every format that is plain memory. Not kept: bitmaps and metafiles
/// as GDI objects (Windows makes bitmaps again from the DIB that is kept),
/// and embedded or linked OLE objects (an Office object copied as such
/// comes back as its text, RTF, HTML and picture, without the live object).
pub(crate) struct Snapshot {
    formats: Vec<(u32, Vec<u8>)>,
    /// Formats that should have been kept but whose data could not be read
    /// (an owner that failed to draw it on demand): the restore is partial.
    unread: usize,
    /// Which of [`MARKERS`] were present, data or not.
    marks: [bool; 4],
}

/// Takes a snapshot of the clipboard, or `None` if it could not be opened
/// or holds more than can sensibly be kept.
pub(crate) fn snapshot(owner: HWND) -> Option<Snapshot> {
    let _open = Open::new(Some(owner))?;
    let mut formats = Vec::new();
    let mut unread = 0;
    let mut total = 0usize;
    let mut format = 0;
    loop {
        format = unsafe { EnumClipboardFormats(format) };
        if format == 0 {
            break;
        }
        if !restorable(format) {
            continue;
        }
        let Some(bytes) = bytes_of(format) else {
            // A marker's presence is kept apart from its data.
            if !is_marker(format) {
                unread += 1;
            }
            continue;
        };
        total += bytes.len();
        if total > SNAPSHOT_LIMIT {
            eprintln!("tack: the clipboard holds over {} MB; it will not be restored", SNAPSHOT_LIMIT >> 20);
            return None;
        }
        formats.push((format, bytes));
    }
    Some(Snapshot { formats, unread, marks: marks() })
}

/// The privacy markers apps put beside a copy. Either of the first two
/// means "do not look"; the last two, set to 0, keep it out of clipboard
/// history and the cloud clipboard.
const MARKERS: [PCWSTR; 4] = [
    w!("ExcludeClipboardContentFromMonitorProcessing"),
    w!("Clipboard Viewer Ignore"),
    w!("CanIncludeInClipboardHistory"),
    w!("CanUploadToCloudClipboard"),
];

/// Which of [`MARKERS`] are on the clipboard, while it is open. Presence is
/// what counts, as an app may set a marker with no data; the last two count
/// unless they say 1 (allowed).
fn marks() -> [bool; 4] {
    let mut marks = [false; 4];
    for (i, &name) in MARKERS.iter().enumerate() {
        let format = registered(name);
        marks[i] = available(format) && (i < 2 || bytes_of(format).is_none_or(|b| b.get(..4) != Some(&[1, 0, 0, 0])));
    }
    marks
}

fn is_marker(format: u32) -> bool {
    MARKERS.iter().any(|&name| registered(name) == format)
}

/// How a restore went, when nothing failed.
pub(crate) enum Restored {
    /// The snapshot is back on the clipboard.
    Done,
    /// Another app wrote to the clipboard after `since`: theirs stays.
    Superseded,
}

/// Puts a snapshot back on the clipboard, unless it changed after `since`
/// (a [`sequence`] number). The restored copy is kept out of Windows'
/// clipboard history and cloud clipboard, which already hold the original,
/// unless the snapshot carries its own markers for that. A copy that was
/// marked private goes back with every marker, whether or not their data
/// could be read, so it stays private.
pub(crate) fn restore(owner: HWND, snapshot: &Snapshot, since: u32) -> Result<Restored, String> {
    let _open = Open::new(Some(owner)).ok_or("the clipboard is busy")?;
    // Checked while it is open, so nobody can write in between.
    if sequence() != since {
        return Ok(Restored::Superseded);
    }
    unsafe { EmptyClipboard() }.map_err(|e| e.to_string())?;
    let private = snapshot.marks.contains(&true);
    let mut failed = snapshot.unread;
    let mut total = snapshot.unread;
    for (format, bytes) in &snapshot.formats {
        if private && is_marker(*format) {
            continue;
        }
        total += 1;
        if set_bytes(*format, bytes).is_err() {
            failed += 1;
        }
    }
    let zero = 0u32.to_le_bytes();
    if private {
        for &name in &MARKERS {
            let format = registered(name);
            total += 1;
            if format == 0 || set_bytes(format, &zero).is_err() {
                failed += 1;
            }
        }
    } else if !snapshot.formats.is_empty() {
        let has = |format: u32| snapshot.formats.iter().any(|(f, _)| *f == format);
        for &name in &MARKERS[2..] {
            let format = registered(name);
            if format != 0 && !has(format) {
                let _ = set_bytes(format, &zero);
            }
        }
    }
    if failed > 0 {
        return Err(format!("{failed} of {total} formats could not be put back"));
    }
    Ok(Restored::Done)
}

/// Sets one memory format, while the clipboard is open.
fn set_bytes(format: u32, bytes: &[u8]) -> Result<(), String> {
    unsafe {
        let global = GlobalAlloc(GMEM_MOVEABLE, bytes.len().max(1)).map_err(|e| e.to_string())?;
        let ptr = GlobalLock(global) as *mut u8;
        if ptr.is_null() {
            let _ = GlobalFree(Some(global));
            return Err("cannot lock memory".into());
        }
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), ptr, bytes.len());
        let _ = GlobalUnlock(global);
        // Once set, the memory belongs to the clipboard.
        if let Err(e) = SetClipboardData(format, Some(HANDLE(global.0))) {
            let _ = GlobalFree(Some(global));
            return Err(e.to_string());
        }
    }
    Ok(())
}

/// What an app put on the clipboard, as Tack would pin it.
pub(crate) enum Content {
    Text(String),
    Image(DynamicImage),
    /// Files copied in Explorer (or any app that copies files).
    Files(Vec<PathBuf>),
    /// Marked private (a password manager's copy): never pinned.
    Private,
    /// Nothing Tack can pin.
    Empty,
}

/// Whether whoever copied the current contents marked them private, while
/// the clipboard is open: a password manager does, so they stay out of
/// clipboard history and clipboard tools. Tack honours all three
/// conventions (kept out of the cloud alone is not private).
fn is_private() -> bool {
    let [exclude, ignore, history, _cloud] = marks();
    exclude || ignore || history
}

/// [`is_private`] for the clipboard as it is now; `None` while another app
/// holds it open.
pub(crate) fn private_now() -> Option<bool> {
    let _open = Open::new(None)?;
    Some(is_private())
}

fn available(format: u32) -> bool {
    format != 0 && unsafe { IsClipboardFormatAvailable(format) }.is_ok()
}

/// Whether the registered format `name` is on the clipboard.
pub(crate) fn has_format(name: PCWSTR) -> bool {
    available(registered(name))
}

/// Any bitmap shows up as CF_DIB, which Windows synthesises from the others.
pub(crate) fn has_picture() -> bool {
    available(CF_DIB.0 as u32) || has_format(w!("PNG"))
}

/// Reads what is on the clipboard now: files, else text, else a picture.
/// Text that is only whitespace or control characters counts as none, so a
/// copied picture that also offers a placeholder character is a picture.
pub(crate) fn read(owner: HWND) -> Content {
    {
        let Some(_open) = Open::new(Some(owner)) else { return Content::Empty };
        if is_private() {
            return Content::Private;
        }
        if let Some(files) = files() {
            if !files.is_empty() {
                return Content::Files(files);
            }
        }
        if let Some(text) = text() {
            if text.chars().any(|c| !c.is_whitespace() && !c.is_control()) {
                return Content::Text(text);
            }
        }
    }
    if has_picture() {
        if let Some(img) = read_image(4, Duration::from_millis(60)) {
            return Content::Image(img);
        }
    }
    Content::Empty
}

/// CF_UNICODETEXT, while the clipboard is open.
fn text() -> Option<String> {
    let bytes = bytes_of(CF_UNICODETEXT.0 as u32)?;
    let units: Vec<u16> = bytes.as_chunks::<2>().0.iter().map(|&pair| u16::from_le_bytes(pair)).collect();
    let end = units.iter().position(|&u| u == 0).unwrap_or(units.len());
    Some(String::from_utf16_lossy(&units[..end]))
}

/// CF_HDROP's paths, while the clipboard is open.
fn files() -> Option<Vec<PathBuf>> {
    unsafe {
        let handle = GetClipboardData(CF_HDROP.0 as u32).ok()?;
        let drop = HDROP(handle.0);
        let count = DragQueryFileW(drop, u32::MAX, None);
        let mut paths = Vec::new();
        for i in 0..count {
            let len = DragQueryFileW(drop, i, None) as usize;
            let mut buf = vec![0u16; len + 1];
            let got = DragQueryFileW(drop, i, Some(&mut buf)) as usize;
            paths.push(PathBuf::from(String::from_utf16_lossy(&buf[..got])));
        }
        Some(paths)
    }
}

/// The clipboard's picture, through arboard (which opens the clipboard
/// itself, so ours must be closed). Tried `tries` times, `gap` apart: the
/// picture may be drawn on demand by its app, or the clipboard still held
/// open by its owner.
pub(crate) fn read_image(tries: u32, gap: Duration) -> Option<DynamicImage> {
    for attempt in 0..tries {
        if attempt > 0 {
            std::thread::sleep(gap);
        }
        let Ok(mut clipboard) = arboard::Clipboard::new() else { continue };
        let Ok(data) = clipboard.get_image() else { continue };
        if let Some(rgba) = RgbaImage::from_raw(data.width as u32, data.height as u32, data.bytes.into_owned()) {
            return Some(tack_core::capture::without_alpha(rgba));
        }
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gdi_and_private_formats_are_never_restored() {
        for format in [2, 3, 9, 14, 0x80, 0x200, 0x2FF, 0x300, 0x3FF] {
            assert!(!restorable(format), "{format:#x}");
        }
        // CF_TEXT, CF_DIB, CF_UNICODETEXT, CF_HDROP, CF_LOCALE, CF_DIBV5.
        for format in [1, 8, 13, 15, 16, 17] {
            assert!(restorable(format), "{format}");
        }
    }

    #[test]
    fn registered_formats_are_restored_unless_they_are_live_ole_objects() {
        let html = registered(w!("HTML Format"));
        let private = registered(w!("ExcludeClipboardContentFromMonitorProcessing"));
        let ole = registered(w!("Ole Private Data"));
        let embed = registered(w!("Embed Source"));
        assert!(restorable(html));
        assert!(restorable(private), "a password manager's marker comes back with its content");
        assert!(!restorable(ole));
        assert!(!restorable(embed));
    }

    #[test]
    fn every_privacy_marker_is_known_and_kept() {
        for name in [
            "ExcludeClipboardContentFromMonitorProcessing",
            "Clipboard Viewer Ignore",
            "CanIncludeInClipboardHistory",
            "CanUploadToCloudClipboard",
        ] {
            let wide: Vec<u16> = name.encode_utf16().chain([0]).collect();
            let format = registered(PCWSTR(wide.as_ptr()));
            assert!(is_marker(format), "{name}");
            assert!(restorable(format), "{name}");
        }
        assert!(!is_marker(registered(w!("HTML Format"))));
    }
}
