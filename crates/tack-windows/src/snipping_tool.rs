//! Hears Snipping Tool captures arrive on the clipboard. With "Automatically
//! save original screenshots" off, Win + Shift + S never writes a file, so
//! watching the Screenshots folder alone would miss them. Only Snipping
//! Tool's own copies count: the clipboard is everyone's, and pinning every
//! copied image would be noise.
//!
//! Debug builds log one line per clipboard change: owner process, foreground
//! app and window class, and the decision. Window titles are never logged, as
//! they can name private documents and chats.

use std::path::{Path, PathBuf};
use std::sync::mpsc::{self, Sender};
use std::sync::OnceLock;
use std::time::Duration;

use image::DynamicImage;
use tack_core::capture::Timestamp;
use windows::core::{w, Error, PCWSTR, PWSTR};
use windows::Win32::Foundation::{
    CloseHandle, ERROR_INSUFFICIENT_BUFFER, HANDLE, HINSTANCE, HWND, LPARAM, LRESULT, WPARAM,
};
use windows::Win32::Storage::Packaging::Appx::GetPackageFamilyName;
use windows::Win32::System::Com::CoTaskMemFree;
use windows::Win32::System::DataExchange::AddClipboardFormatListener;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::SystemInformation::{GetLocalTime, GetSystemWindowsDirectoryW};
use windows::Win32::System::Threading::{
    OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
};
use windows::Win32::UI::Shell::{FOLDERID_ProgramFiles, SHGetKnownFolderPath, KF_FLAG_DEFAULT};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DispatchMessageW, GetForegroundWindow, GetMessageW, RegisterClassW, HWND_MESSAGE,
    MSG, WINDOW_EX_STYLE, WINDOW_STYLE, WM_CLIPBOARDUPDATE, WNDCLASSW,
};

use crate::focus::{class_name, window_process};
use crate::{capture_origin, clipboard};

/// Snipping Tool's package family, the same on every PC: its publisher's
/// signature is part of it, so no other app can have it.
const SNIPPING_FAMILY: &str = "Microsoft.ScreenSketch_8wekyb3d8bbwe";
/// The programs that put Win + Shift + S captures on the clipboard, trusted
/// by name only where only Windows installs programs (the old Snipping
/// Tool in System32, the capture overlay in SystemApps).
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
    /// The clipboard's sequence number then. The image is pinned only if it
    /// is the same once read, so it is the very copy that was judged here.
    sequence: u32,
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
            for Accepted { line, pointer, sequence } in rx {
                let decision = match read(sequence) {
                    Ok(img) => on_capture(img, pointer),
                    Err(why) => why.into(),
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

/// Reads the capture heard at `sequence`: not if it was marked private, and
/// only if nothing else reached the clipboard in the meantime.
fn read(sequence: u32) -> Result<DynamicImage, &'static str> {
    let replaced = "ignored (replaced before it was read)";
    let mut private = None;
    for attempt in 0..READ_TRIES {
        if attempt > 0 {
            std::thread::sleep(READ_GAP);
        }
        private = clipboard::private_now();
        if private.is_some() {
            break;
        }
    }
    match private {
        None => return Err("ignored (the clipboard stayed busy)"),
        Some(true) => return Err("ignored (marked private)"),
        Some(false) => {}
    }
    if clipboard::sequence() != sequence {
        return Err(replaced);
    }
    let img = clipboard::read_image(READ_TRIES, READ_GAP).ok_or("ignored (unreadable)")?;
    if clipboard::sequence() != sequence {
        return Err(replaced);
    }
    Ok(img)
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
    // First, so the checks below judge this very change or a later one,
    // which the reader then turns down.
    let sequence = clipboard::sequence();
    let pointer = capture_origin::pointer();
    let owner = clipboard::owner();
    // Only debug builds log, so only they look at the foreground window.
    let line = if cfg!(debug_assertions) {
        let fg = unsafe { GetForegroundWindow() };
        let class = class_name(fg);
        format!(
            "owner={} fg={} class={}",
            owner.and_then(window_exe).as_deref().unwrap_or("none"),
            window_exe(fg).as_deref().unwrap_or("none"),
            if class.is_empty() { "none" } else { &class },
        )
    } else {
        String::new()
    };
    let ignored = if crate::selection::grabbing() {
        // Pinning the selection copies and restores the clipboard itself.
        Some("ignored (pinning the selection)")
    } else if !owner.is_some_and(is_snipping_tool) {
        Some("ignored")
    } else if !clipboard::has_picture() {
        // Snipping Tool also copies text, from its text actions.
        Some("ignored (no image)")
    } else {
        None
    };
    match (ignored, ACCEPTED.get()) {
        (None, Some(tx)) => {
            let _ = tx.send(Accepted { line, pointer, sequence });
        }
        (decision, _) => log(&line, decision.unwrap_or("ignored")),
    }
}

/// Whether the window in front belongs to Snipping Tool, such as the
/// full-screen overlay Win + Shift + S puts up while a region is picked.
/// Windows can report that overlay as a full-screen app.
pub fn in_front() -> bool {
    is_snipping_tool(unsafe { GetForegroundWindow() })
}

/// Whether a window is Snipping Tool's (or its capture overlay's): by its
/// package, or by its program's full path. A name alone would trust any
/// program called SnippingTool.exe.
fn is_snipping_tool(hwnd: HWND) -> bool {
    let Some(process) = Process::of(hwnd) else { return false };
    process.family().as_deref() == Some(SNIPPING_FAMILY)
        || process.image().is_some_and(|path| trusted_snipper(&path, trusted_folders()))
}

/// Whether `path` is one of [`SNIPPERS`] inside one of `folders` (each
/// lowercase, ending in a backslash).
fn trusted_snipper(path: &str, folders: &[String]) -> bool {
    let path = path.to_lowercase();
    let name = Path::new(&path).file_name().and_then(|n| n.to_str()).unwrap_or_default();
    SNIPPERS.contains(&name) && !path.contains("\\..\\") && folders.iter().any(|f| path.starts_with(f.as_str()))
}

/// Where only Windows puts programs: System32, SystemApps, and the folder
/// Store packages are installed to.
fn trusted_folders() -> &'static [String] {
    static FOLDERS: OnceLock<Vec<String>> = OnceLock::new();
    FOLDERS.get_or_init(|| {
        let mut folders = Vec::new();
        let mut buf = [0u16; 260];
        let len = unsafe { GetSystemWindowsDirectoryW(Some(&mut buf)) } as usize;
        if len > 0 && len < buf.len() {
            let windows = String::from_utf16_lossy(&buf[..len]).trim_end_matches('\\').to_lowercase();
            folders.push(format!("{windows}\\system32\\"));
            folders.push(format!("{windows}\\systemapps\\"));
        }
        unsafe {
            if let Ok(p) = SHGetKnownFolderPath(&FOLDERID_ProgramFiles, KF_FLAG_DEFAULT, None) {
                if let Ok(program_files) = p.to_string() {
                    folders.push(format!("{}\\windowsapps\\", program_files.trim_end_matches('\\').to_lowercase()));
                }
                CoTaskMemFree(Some(p.0 as *const _));
            }
        }
        folders
    })
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
    let path = PathBuf::from(Process::of(hwnd)?.image()?);
    path.file_name().map(|n| n.to_string_lossy().into_owned())
}

/// A window's process, open to ask about it; closed when dropped.
struct Process(HANDLE);

impl Process {
    fn of(hwnd: HWND) -> Option<Process> {
        let pid = window_process(hwnd)?;
        unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) }.ok().map(Process)
    }

    /// The full path of its program.
    fn image(&self) -> Option<String> {
        let mut buf = [0u16; 1024];
        let mut len = buf.len() as u32;
        unsafe { QueryFullProcessImageNameW(self.0, PROCESS_NAME_WIN32, PWSTR(buf.as_mut_ptr()), &mut len) }.ok()?;
        Some(String::from_utf16_lossy(&buf[..len as usize]))
    }

    /// Its package family, for a packaged app.
    fn family(&self) -> Option<String> {
        let mut len = 0u32;
        if unsafe { GetPackageFamilyName(self.0, &mut len, None) } != ERROR_INSUFFICIENT_BUFFER {
            return None;
        }
        let mut buf = vec![0u16; len as usize];
        unsafe { GetPackageFamilyName(self.0, &mut len, Some(PWSTR(buf.as_mut_ptr()))) }.ok().ok()?;
        let end = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
        Some(String::from_utf16_lossy(&buf[..end]))
    }
}

impl Drop for Process {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn snipping_tool_is_trusted_only_where_windows_installs_it() {
        let folders = ["c:\\windows\\system32\\".to_string(), "c:\\program files\\windowsapps\\".to_string()];
        assert!(trusted_snipper("C:\\Windows\\System32\\SnippingTool.exe", &folders));
        assert!(trusted_snipper(
            "C:\\Program Files\\WindowsApps\\Microsoft.ScreenSketch_11.2409.25.0_x64__8wekyb3d8bbwe\\SnippingTool\\ScreenClippingHost.exe",
            &folders
        ));
        assert!(!trusted_snipper("C:\\Users\\me\\Downloads\\SnippingTool.exe", &folders));
        assert!(!trusted_snipper("C:\\Windows\\System32\\..\\Temp\\SnippingTool.exe", &folders));
        assert!(!trusted_snipper("C:\\Windows\\System32\\notepad.exe", &folders));
        assert!(!trusted_snipper("C:\\Windows\\System32Evil\\SnippingTool.exe", &folders));
    }
}
