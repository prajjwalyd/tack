//! Pinning things on purpose: the selection (Win+Alt+C) and whatever is
//! dropped on the board. Text becomes a note, a picture a print, image files
//! prints of those files. Anything that cannot be pinned gets a short
//! notice on the board instead.
//!
//! Text only ever reaches the board this way, because the user asked. The
//! clipboard is read here only right after the pin shortcut, and what the
//! user had on it before is put back (`tack_windows::selection`).

use std::path::{Component, Path, PathBuf, Prefix};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use base64::Engine;
use tack_core::{capture, files, note, thumbnail, Origin, RevealReason};
use tack_windows::selection::{self, Clipboard, Grabbed, Target};
use tack_windows::snipping_tool;
use tauri::AppHandle;

use crate::captures;
use crate::ipc::events;
use crate::prints::{self, Pin};
use crate::reveal::{self, Placement};
use crate::state::lock;

/// How long the board stays down for a notice alone.
const NOTICE_PEEK: Duration = Duration::from_millis(1600);
/// The largest picture accepted from a drop.
const MAX_DROP_BYTES: usize = 40 * 1024 * 1024;

/// What the board says when something could not be pinned.
pub mod say {
    pub const NOTHING_SELECTED: &str = "Nothing selected";
    pub const ALREADY_PINNED: &str = "Already pinned";
    pub const PRIVATE: &str = "Not pinned: marked private";
    pub const NOT_PINNABLE: &str = "Only images and text can be pinned";
    pub const BAD_IMAGE: &str = "Can't pin that image";
    pub const FAILED: &str = "Couldn't pin that";
    pub const NOT_RESTORED: &str = "Couldn't restore your clipboard";
}

/// Brings the board down briefly (unless it is down already or something is
/// full screen) and shows `text` under it. From a worker thread: the
/// full-screen check may wait a moment.
pub fn notice(app: &AppHandle, text: &str) {
    let (ready, shown) = {
        let s = lock(app);
        (s.ui_ready, s.view.shown)
    };
    if !ready {
        return;
    }
    if !shown {
        if !reveal::new_may_reveal(app) {
            return;
        }
        reveal::show(app, RevealReason::New, Placement::under_pointer(), NOTICE_PEEK);
    }
    events::notice(app, text);
}

/// Set while a selection is pinned: one at a time, and a press meanwhile is
/// dropped (two grabs would snapshot and restore each other's copies).
static PINNING: AtomicBool = AtomicBool::new(false);

/// Holds [`PINNING`] until dropped.
struct Pinning;

impl Pinning {
    fn start() -> Option<Pinning> {
        PINNING.compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst).ok().map(|_| Pinning)
    }
}

impl Drop for Pinning {
    fn drop(&mut self) {
        PINNING.store(false, Ordering::SeqCst);
    }
}

/// The pin shortcut was pressed over `target` (the window in front as it
/// fired): copies the selection there and pins what it was. Blocks for up
/// to about two seconds; runs on a thread of its own.
pub fn pin_selection(app: &AppHandle, target: Option<Target>) -> String {
    let Some(_pinning) = Pinning::start() else {
        return "ignored (still pinning the last selection)".into();
    };
    if !lock(app).ui_ready {
        return "ignored (the board is not ready)".into();
    }
    let grab = selection::grab(target);
    let restored = match &grab.clipboard {
        Clipboard::Untouched => "untouched",
        Clipboard::Restored => "restored",
        Clipboard::LeftNewer => "left as another app set it",
        Clipboard::Failed(e) => {
            eprintln!("tack: the clipboard could not be put back as it was: {e}");
            "NOT restored"
        }
    };
    let outcome = match grab.what {
        Grabbed::Text(text) => pin_text(app, &text),
        Grabbed::Image(img) => {
            let decision = captures::pin_picture(app, img);
            format!("picture: {decision}")
        }
        Grabbed::Files(paths) => pin_files(app, paths),
        Grabbed::Nothing => {
            notice(app, say::NOTHING_SELECTED);
            "nothing selected".into()
        }
        Grabbed::Private => {
            notice(app, say::PRIVATE);
            "private, not pinned".into()
        }
        Grabbed::KeysHeld => "keys held too long, nothing sent".into(),
    };
    if let Clipboard::Failed(_) = grab.clipboard {
        // Last, so it is the notice that stays: the user's next paste
        // will not be what they expect.
        notice(app, say::NOT_RESTORED);
    }
    let line = format!("pin selection: {outcome}; clipboard {restored}");
    trace!("{line}");
    line
}

/// Pins `text` as a note (a link if it is one), live: it presses onto the
/// board, which comes down for it. Returns what happened, for the log.
pub fn pin_text(app: &AppHandle, text: &str) -> String {
    let Some(body) = note::body(text) else {
        notice(app, say::NOTHING_SELECTED);
        return "blank text".into();
    };
    if lock(app).board.find_note(&body.text).is_some() {
        notice(app, say::ALREADY_PINNED);
        return "already pinned".into();
    }
    let path = match note::save(&body, &note::notes_folder(), snipping_tool::local_time()) {
        Ok(path) => path,
        Err(e) => {
            eprintln!("tack: cannot save a note: {e}");
            notice(app, say::FAILED);
            return format!("cannot save ({e})");
        }
    };
    let chars = body.text.chars().count();
    let link = body.link.is_some();
    match prints::pin_note(app, path.clone(), body, prints::live()) {
        Pin::New(aged) => {
            reveal::reveal(app, RevealReason::New);
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || aged.announce(&handle));
            format!("{} of {chars} characters pinned", if link { "link" } else { "note" })
        }
        _ => {
            // Never shown: the same text was pinned a moment ago.
            let _ = std::fs::remove_file(&path);
            notice(app, say::ALREADY_PINNED);
            "already pinned".into()
        }
    }
}

/// Pins the image files among `paths` where they are, like screenshot files.
/// Other files are not pinned (yet).
fn pin_files(app: &AppHandle, paths: Vec<PathBuf>) -> String {
    let total = paths.len();
    let images: Vec<PathBuf> =
        paths.into_iter().filter(|p| on_a_drive(p) && files::is_image(p) && p.is_file()).collect();
    if images.is_empty() {
        notice(app, say::NOT_PINNABLE);
        return format!("{total} file(s), none of them PNG or JPEG");
    }
    let mut pinned = 0;
    let mut already = 0;
    for path in images {
        let Ok(thumb) = thumbnail::make(&path) else { continue };
        match prints::pin(app, path, thumb, Origin::Folder, prints::live(), None) {
            Pin::New(aged) => {
                pinned += 1;
                let handle = app.clone();
                let _ = app.run_on_main_thread(move || aged.announce(&handle));
            }
            Pin::Exists => already += 1,
            _ => {}
        }
    }
    if pinned > 0 {
        reveal::reveal(app, RevealReason::New);
    } else if already > 0 {
        notice(app, say::ALREADY_PINNED);
    } else {
        notice(app, say::BAD_IMAGE);
    }
    format!("{pinned} of {total} file(s) pinned")
}

/// Whether a copied file's path is on a drive letter (`C:\...`). Anything
/// else is refused before it is touched: a UNC path (`\\server\share`, or
/// one an app put there on purpose) would make Windows connect to that
/// server and offer it the user's sign-in, and `\\?\` and `\\.\` paths
/// reach devices and the same servers.
fn on_a_drive(path: &Path) -> bool {
    matches!(path.components().next(), Some(Component::Prefix(p)) if matches!(p.kind(), Prefix::Disk(_)))
        && path.is_absolute()
}

/// A picture dropped on the board: the bytes of a PNG or JPEG file, in
/// base64. Saved unchanged in Tack's captures folder and pinned like a
/// capture, so it goes to the Recycle Bin when it leaves the board.
pub fn pin_dropped_image(app: &AppHandle, name: &str, data: &str) -> String {
    let data = data.trim();
    // Too long to be a picture Tack takes: refused before it is decoded.
    if data.len() > MAX_DROP_BYTES.div_ceil(3) * 4 {
        notice(app, say::BAD_IMAGE);
        return format!("{name}: too large");
    }
    let bytes = match base64::engine::general_purpose::STANDARD.decode(data) {
        Ok(bytes) => bytes,
        _ => {
            notice(app, say::BAD_IMAGE);
            return format!("{name}: not a picture Tack can take");
        }
    };
    pin_image_bytes(app, name, &bytes).unwrap_or_else(|line| line)
}

/// Pins the bytes of a PNG or JPEG file, saved unchanged in Tack's captures
/// folder, like a capture. Ok with the log's line when it went up; Err with
/// it (and a notice on the board) when it did not.
pub fn pin_image_bytes(app: &AppHandle, name: &str, bytes: &[u8]) -> Result<String, String> {
    if bytes.len() > MAX_DROP_BYTES {
        notice(app, say::BAD_IMAGE);
        return Err(format!("{name}: too large"));
    }
    let ext = if bytes.starts_with(b"\x89PNG\r\n\x1a\n") {
        "png"
    } else if bytes.starts_with(&[0xFF, 0xD8, 0xFF]) {
        "jpg"
    } else {
        notice(app, say::BAD_IMAGE);
        return Err(format!("{name}: neither PNG nor JPEG"));
    };
    let thumb = match thumbnail::decode_bytes(bytes).and_then(thumbnail::from_image) {
        Ok(thumb) => thumb,
        Err(e) => {
            notice(app, say::BAD_IMAGE);
            return Err(format!("{name}: cannot decode ({e})"));
        }
    };
    let saved = capture::save_bytes(bytes, &capture::captures_folder(), "Image", ext, snipping_tool::local_time());
    let path = match saved {
        Ok(path) => path,
        Err(e) => {
            notice(app, say::FAILED);
            return Err(format!("{name}: cannot save ({e})"));
        }
    };
    match prints::pin(app, path.clone(), thumb, Origin::Capture, prints::live(), None) {
        Pin::New(aged) => {
            reveal::reveal(app, RevealReason::New);
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || aged.announce(&handle));
            Ok(format!("{name}: pinned"))
        }
        _ => {
            prints::discard_captures(vec![path]);
            Err(format!("{name}: not pinned"))
        }
    }
}

/// Debug builds only: lets a script pin the selection of a window it
/// started, without anyone's keyboard.
#[cfg(debug_assertions)]
pub mod debug {
    use std::time::Duration;

    use tack_windows::focus;
    use tack_windows::selection::Target;
    use tauri::AppHandle;

    /// With `TACK_DEBUG_CONTROL=1`, creating `%TEMP%\tack-debug\pin-selection`
    /// holding a process id brings that process's window to the front and,
    /// only if it really is in front, runs the pin-selection routine on it
    /// (Ctrl+C goes to that window and nowhere else), then gives the
    /// foreground back. Logs a `pin-selection test:` line.
    pub fn start(app: &AppHandle) {
        if std::env::var("TACK_DEBUG_CONTROL").as_deref() != Ok("1") {
            return;
        }
        let dir = std::env::temp_dir().join("tack-debug");
        let _ = std::fs::create_dir_all(&dir);
        let app = app.clone();
        std::thread::spawn(move || loop {
            std::thread::sleep(Duration::from_millis(200));
            // `reveal-keyboard`: as the show-or-hide shortcut would.
            if std::fs::remove_file(dir.join("reveal-keyboard")).is_ok() {
                eprintln!("tack: keyboard test: foreground process before: {:?}", focus::foreground_process());
                crate::reveal::reveal(&app, tack_core::RevealReason::Hotkey);
            }
            let path = dir.join("pin-selection");
            let Ok(text) = std::fs::read_to_string(&path) else { continue };
            if std::fs::remove_file(&path).is_err() {
                continue;
            }
            let Ok(pid) = text.trim().parse::<u32>() else {
                eprintln!("tack: pin-selection test: FAIL (the file must hold a process id)");
                continue;
            };
            let line = match focus::bring_to_front(pid) {
                Some(previous) => {
                    // Keys go to the target only while it stays in front.
                    let target = Target::in_front().filter(|t| t.pid() == pid);
                    let line = match target {
                        Some(target) => super::pin_selection(&app, Some(target)),
                        None => "FAIL (the window lost the front; no keys were sent)".into(),
                    };
                    focus::return_foreground(previous);
                    line
                }
                None => "FAIL (could not bring the window to the front; no keys were sent)".into(),
            };
            eprintln!("tack: pin-selection test: {line}");
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_files_on_a_drive_are_opened() {
        assert!(on_a_drive(Path::new(r"C:\Users\me\Pictures\a.png")));
        assert!(on_a_drive(Path::new(r"Z:\shots\b.jpg")), "a mapped drive is the user's own");
        for path in [
            r"\server\share\a.png",
            r"//server/share/a.png",
            r"\?\UNC\server\share\a.png",
            r"\?\C:\Users\me\a.png",
            r"\.\pipe\a.png",
            r"C:a.png",
            r"a.png",
        ] {
            assert!(!on_a_drive(Path::new(path)), "{path}");
        }
    }
}
