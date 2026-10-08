//! Talking to the Windows shell on a print's behalf: opening and editing its
//! file, opening a note's link in the browser, showing it in Explorer,
//! moving it to the Recycle Bin, and putting its image or text on the
//! clipboard.

use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};

use windows::core::{HSTRING, PCWSTR};
use windows::Win32::Foundation::MAX_PATH;
use windows::Win32::System::Com::{CoInitializeEx, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE};
use windows::Win32::System::SystemInformation::GetWindowsDirectoryW;
use windows::Win32::UI::Shell::ShellExecuteW;
use windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

/// Runs `job` on a short-lived thread of its own. If none can be started,
/// the action is logged and dropped rather than taking Tack down.
fn spawn(name: &str, job: impl FnOnce() + Send + 'static) {
    if let Err(e) = std::thread::Builder::new().name(name.into()).spawn(job) {
        eprintln!("tack: cannot start {name}: {e}");
    }
}

/// ShellExecute may hand the file to a shell extension that needs COM, and
/// can take a moment, so it runs on its own thread.
fn shell_execute(verb: &'static str, path: PathBuf, fallback: Option<&'static str>) {
    spawn("tack-shell", move || unsafe {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
        let file = HSTRING::from(path.as_os_str());
        let run = |verb: &str| {
            let verb = HSTRING::from(verb);
            ShellExecuteW(None, &verb, &file, PCWSTR::null(), PCWSTR::null(), SW_SHOWNORMAL).0 as isize
        };
        // Values up to 32 are errors, such as no app registered for the verb.
        if run(verb) <= 32 {
            if let Some(fallback) = fallback {
                run(fallback);
            }
        }
    });
}

/// Opens with the default app.
pub fn open(path: &Path) {
    shell_execute("open", path.to_path_buf(), None);
}

/// The "edit" verb, which is Paint for images. Opens normally if no editor
/// is registered.
pub fn edit(path: &Path) {
    shell_execute("edit", path.to_path_buf(), Some("open"));
}

/// Opens a web link in the default browser. Only `http` and `https` links
/// (see `tack_core::note::link_of`) are ever passed in: anything else handed
/// to ShellExecute could start a program.
pub fn open_url(url: &str) {
    let lower = url.to_ascii_lowercase();
    if !(lower.starts_with("https://") || lower.starts_with("http://")) || url.chars().any(char::is_whitespace) {
        eprintln!("tack: not opening a link that is not http or https");
        return;
    }
    shell_execute("open", PathBuf::from(url), None);
}

pub fn open_folder(path: &Path) {
    shell_execute("open", path.to_path_buf(), None);
}

/// `%WINDIR%\explorer.exe`, by its full path from Windows itself: a bare
/// name would be looked for in the current folder and on PATH first.
pub fn explorer_exe() -> Option<PathBuf> {
    let mut buf = [0u16; MAX_PATH as usize];
    let len = unsafe { GetWindowsDirectoryW(Some(&mut buf)) } as usize;
    // 0 is a failure; more than the buffer is a size it would have needed.
    if len == 0 || len >= buf.len() {
        return None;
    }
    Some(PathBuf::from(OsString::from_wide(&buf[..len])).join("explorer.exe"))
}

/// Opens Explorer with the file selected.
pub fn show_in_explorer(path: &Path) {
    let Some(explorer) = explorer_exe() else {
        eprintln!("tack: cannot find the Windows folder to start Explorer");
        return;
    };
    // Explorer parses its own command line, so the quotes are passed raw.
    let _ = std::process::Command::new(explorer).raw_arg(format!("/select,\"{}\"", path.display())).spawn();
}

/// Moves the file to the Recycle Bin, on a thread of its own: the shell can
/// take a moment.
pub fn recycle(path: PathBuf) {
    spawn("tack-recycle", move || {
        if let Err(e) = trash::delete(&path) {
            eprintln!("tack: cannot recycle {}: {e}", path.display());
        }
    });
}

/// Puts the full image (not the thumbnail) on the clipboard. Slow for a big
/// screenshot, so call it off the UI thread.
pub fn copy_image(path: &Path) -> Result<(), String> {
    let rgba = tack_core::thumbnail::decode(path)?.to_rgba8();
    let (width, height) = rgba.dimensions();
    let mut clipboard = arboard::Clipboard::new().map_err(|e| e.to_string())?;
    clipboard
        .set_image(arboard::ImageData { width: width as usize, height: height as usize, bytes: rgba.into_raw().into() })
        .map_err(|e| e.to_string())
}

/// Puts text on the clipboard, with Windows line breaks.
pub fn copy_text(text: &str) -> Result<(), String> {
    let text = text.replace("\r\n", "\n").replace('\n', "\r\n");
    let mut clipboard = arboard::Clipboard::new().map_err(|e| e.to_string())?;
    clipboard.set_text(text).map_err(|e| e.to_string())
}
