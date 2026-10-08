//! Talking to the Windows shell on a print's behalf: opening and editing its
//! file, showing it in Explorer, moving it to the Recycle Bin, and putting
//! its image on the clipboard.

use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};

use windows::core::{HSTRING, PCWSTR};
use windows::Win32::System::Com::{CoInitializeEx, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE};
use windows::Win32::UI::Shell::ShellExecuteW;
use windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

/// ShellExecute may hand the file to a shell extension that needs COM, and
/// can take a moment, so it runs on its own thread.
fn shell_execute(verb: &'static str, path: PathBuf, fallback: Option<&'static str>) {
    std::thread::spawn(move || unsafe {
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

pub fn open_folder(path: &Path) {
    shell_execute("open", path.to_path_buf(), None);
}

/// Opens Explorer with the file selected.
pub fn show_in_explorer(path: &Path) {
    // Explorer parses its own command line, so the quotes are passed raw.
    let _ = std::process::Command::new("explorer.exe").raw_arg(format!("/select,\"{}\"", path.display())).spawn();
}

/// Moves the file to the Recycle Bin, on a thread of its own: the shell can
/// take a moment.
pub fn recycle(path: PathBuf) {
    std::thread::spawn(move || {
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
