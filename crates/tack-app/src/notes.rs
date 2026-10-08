//! Pinning things on purpose: the selection (Win+Alt+C) and whatever is
//! dropped on the board. Text becomes a note, a picture a print, image files
//! prints of those files. Anything that cannot be pinned gets a short
//! notice on the board instead.
//!
//! Text only ever reaches the board this way, because the user asked. The
//! clipboard is read here only right after the pin shortcut, and what the
//! user had on it before is put back (`tack_windows::selection`).

use std::path::PathBuf;
use std::time::Duration;

use base64::Engine;
use tack_core::{capture, files, note, thumbnail, Origin, RevealReason};
use tack_windows::selection::{self, Grabbed};
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

/// The pin shortcut was pressed: copies the selection in the app in front
/// and pins what it was. Blocks for up to about two seconds; runs on a
/// thread of its own.
pub fn pin_selection(app: &AppHandle) -> String {
    pin_selection_of(app, None)
}

/// [`pin_selection`], sending Ctrl+C only if the window in front belongs to
/// process `only` (when given).
fn pin_selection_of(app: &AppHandle, only: Option<u32>) -> String {
    if !lock(app).ui_ready {
        return "ignored (the board is not ready)".into();
    }
    let grab = selection::grab(only);
    if let Some(Err(e)) = &grab.restored {
        eprintln!("tack: the clipboard could not be put back as it was: {e}");
    }
    let restored = match &grab.restored {
        None => "untouched",
        Some(Ok(())) => "restored",
        Some(Err(_)) => "NOT restored",
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
    let images: Vec<PathBuf> = paths.into_iter().filter(|p| files::is_image(p) && p.is_file()).collect();
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

/// A picture dropped on the board: the bytes of a PNG or JPEG file, in
/// base64. Saved unchanged in Tack's captures folder and pinned like a
/// capture, so it goes to the Recycle Bin when it leaves the board.
pub fn pin_dropped_image(app: &AppHandle, name: &str, data: &str) -> String {
    let bytes = match base64::engine::general_purpose::STANDARD.decode(data.trim()) {
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
/// started, without anyone's keyboard. See docs/performance.md.
#[cfg(debug_assertions)]
pub mod debug {
    use std::time::Duration;

    use tack_windows::focus;
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
                    let line = super::pin_selection_of(&app, Some(pid));
                    focus::return_foreground(previous);
                    line
                }
                None => "FAIL (could not bring the window to the front; no keys were sent)".into(),
            };
            eprintln!("tack: pin-selection test: {line}");
        });
    }
}
