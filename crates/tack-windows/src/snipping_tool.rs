//! Hears Snipping Tool captures arrive on the clipboard. With "Automatically
//! save original screenshots" off, Win + Shift + S never writes a file, so
//! watching the Screenshots folder alone would miss them. Only Snipping
//! Tool's own copies count: the clipboard is everyone's, and pinning every
//! copied image would be noise.
//!
//! Debug builds log one line per clipboard change: owner process, foreground
//! app and window class, and the decision. Window titles are never logged, as
//! they can name private documents and chats.

use std::path::PathBuf;
use std::sync::mpsc::{self, Sender};
use std::sync::OnceLock;
use std::time::Duration;

use image::DynamicImage;
use tack_core::capture::Timestamp;
use windows::core::{w, Error, PCWSTR, PWSTR};
use windows::Win32::Foundation::{CloseHandle, HINSTANCE, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::DataExchange::{AddClipboardFormatListener, GetClipboardOwner};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::SystemInformation::GetLocalTime;
use windows::Win32::System::Threading::{
    OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DispatchMessageW, GetClassNameW, GetForegroundWindow, GetMessageW,
    GetWindowThreadProcessId, RegisterClassW, HWND_MESSAGE, MSG, WINDOW_EX_STYLE, WINDOW_STYLE, WM_CLIPBOARDUPDATE,
    WNDCLASSW,
};

use crate::{capture_origin, clipboard};

/// The processes that put Win + Shift + S captures on the clipboard.
const SNIPPERS: [&str; 2] = ["snippingtool.exe", "screenclippinghost.exe"];
/// Reading the image is retried for about a second.
const READ_TRIES: u32 = 6;
const READ_GAP: Duration = Duration::from_millis(200);

/// Accepted clipboard changes, handed to the reader thread with the start of
/// their log line.
static ACCEPTED: OnceLock<Sender<Accepted>> = OnceLock::new();

/// A clipboard change that is a capture, as heard.
struct Accepted {
    line: String,
    /// The pointer (physical px) the moment the change was heard: normally
    /// still on the corner of the selection where the drag ended.
    pointer: (i32, i32),
}

/// Starts listening. `on_capture` gets each Snipping Tool image, with where
/// the pointer was (physical px) when it arrived, in the order they came, on
/// a thread of its own, and returns what it did with it for the log ("new",
/// "duplicate", ...). Only the first call does anything.
pub fn start(mut on_capture: impl FnMut(DynamicImage, (i32, i32)) -> String + Send + 'static) {
    let (tx, rx) = mpsc::channel::<Accepted>();
    if ACCEPTED.set(tx).is_err() {
        return;
    }
    // One reader, so captures are saved and pinned in the order they came.
    std::thread::Builder::new()
        .name("tack-capture".into())
        .spawn(move || {
            for Accepted { line, pointer } in rx {
                let decision = match clipboard::read_image(READ_TRIES, READ_GAP) {
                    Some(img) => on_capture(img, pointer),
                    None => "ignored (unreadable)".into(),
                };
                log(&line, &decision);
            }
        })
        .expect("capture thread");
    std::thread::Builder::new()
        .name("tack-clipboard".into())
        .spawn(|| {
            if let Err(e) = unsafe { listen() } {
                eprintln!("tack: cannot watch the clipboard: {e}");
            }
        })
        .expect("clipboard thread");
}

/// The local time, to name a capture's file after.
pub fn local_time() -> Timestamp {
    let t = unsafe { GetLocalTime() };
    Timestamp { year: t.wYear, month: t.wMonth, day: t.wDay, hour: t.wHour, minute: t.wMinute, second: t.wSecond }
}

/// A message-only window that hears every clipboard change, on a thread of
/// its own with its own message loop.
unsafe fn listen() -> windows::core::Result<()> {
    let instance: HINSTANCE = GetModuleHandleW(PCWSTR::null())?.into();
    let class = w!("TackClipboard");
    let wc = WNDCLASSW { lpfnWndProc: Some(wndproc), hInstance: instance, lpszClassName: class, ..Default::default() };
    if RegisterClassW(&wc) == 0 {
        return Err(Error::from_thread());
    }
    let hwnd = CreateWindowExW(
        WINDOW_EX_STYLE::default(),
        class,
        w!("Tack clipboard"),
        WINDOW_STYLE::default(),
        0,
        0,
        0,
        0,
        Some(HWND_MESSAGE),
        None,
        Some(instance),
        None,
    )?;
    AddClipboardFormatListener(hwnd)?;
    let mut msg = MSG::default();
    while GetMessageW(&mut msg, None, 0, 0).0 > 0 {
        DispatchMessageW(&msg);
    }
    Ok(())
}

unsafe extern "system" fn wndproc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if msg == WM_CLIPBOARDUPDATE {
        changed();
        return LRESULT(0);
    }
    DefWindowProcW(hwnd, msg, wparam, lparam)
}

/// Decides on the spot whether a change is a capture: the owner and the
/// foreground window are only meaningful right now. Reading the image is
/// slow and left to the reader thread.
fn changed() {
    let pointer = capture_origin::pointer();
    let owner = unsafe { GetClipboardOwner() }.ok().filter(|h| !h.is_invalid());
    let owner_exe = owner.and_then(window_exe);
    // Only debug builds log, so only they look at the foreground window.
    let line = if cfg!(debug_assertions) {
        let fg = unsafe { GetForegroundWindow() };
        format!(
            "owner={} fg={} class={}",
            owner_exe.as_deref().unwrap_or("none"),
            window_exe(fg).as_deref().unwrap_or("none"),
            class_name(fg),
        )
    } else {
        String::new()
    };
    let snipper = owner_exe.is_some_and(|exe| is_snipper(&exe));
    let ignored = if crate::selection::grabbing() {
        // Pinning the selection copies and restores the clipboard itself.
        Some("ignored (pinning the selection)")
    } else if !snipper {
        Some("ignored")
    } else if clipboard::has_format(w!("ExcludeClipboardContentFromMonitorProcessing")) {
        // Marked private by whoever copied it.
        Some("ignored (excluded)")
    } else if !clipboard::has_picture() {
        // Snipping Tool also copies text, from its text actions.
        Some("ignored (no image)")
    } else {
        None
    };
    match (ignored, ACCEPTED.get()) {
        (None, Some(tx)) => {
            let _ = tx.send(Accepted { line, pointer });
        }
        (decision, _) => log(&line, decision.unwrap_or("ignored")),
    }
}

/// Whether the window in front belongs to Snipping Tool, such as the
/// full-screen overlay Win + Shift + S puts up while a region is picked.
/// Windows can report that overlay as a full-screen app.
pub fn in_front() -> bool {
    front_exe().is_some_and(|exe| is_snipper(&exe))
}

fn is_snipper(exe: &str) -> bool {
    SNIPPERS.contains(&exe.to_ascii_lowercase().as_str())
}

/// The file name of the app whose window is in front, e.g. "Code.exe".
pub fn front_exe() -> Option<String> {
    window_exe(unsafe { GetForegroundWindow() })
}

/// One line per clipboard change, in debug builds only.
fn log(line: &str, decision: &str) {
    if cfg!(debug_assertions) {
        eprintln!("tack: clipboard {line} -> {decision}");
    }
}

/// The file name of the process that owns a window, e.g. "SnippingTool.exe".
fn window_exe(hwnd: HWND) -> Option<String> {
    if hwnd.is_invalid() {
        return None;
    }
    unsafe {
        let mut pid = 0u32;
        GetWindowThreadProcessId(hwnd, Some(&mut pid));
        if pid == 0 {
            return None;
        }
        let process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid).ok()?;
        let mut buf = [0u16; 1024];
        let mut len = buf.len() as u32;
        let ok = QueryFullProcessImageNameW(process, PROCESS_NAME_WIN32, PWSTR(buf.as_mut_ptr()), &mut len).is_ok();
        let _ = CloseHandle(process);
        if !ok {
            return None;
        }
        let path = PathBuf::from(String::from_utf16_lossy(&buf[..len as usize]));
        path.file_name().map(|n| n.to_string_lossy().into_owned())
    }
}

fn class_name(hwnd: HWND) -> String {
    if hwnd.is_invalid() {
        return "none".into();
    }
    let mut buf = [0u16; 256];
    let len = unsafe { GetClassNameW(hwnd, &mut buf) }.max(0) as usize;
    String::from_utf16_lossy(&buf[..len])
}
