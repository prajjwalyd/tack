//! Screenshot files: what a change in the Screenshots folder means for the
//! board. New image files get pinned and the board peeks out; an edit to a
//! pinned file refreshes its thumbnail; if a pinned file is deleted or moved
//! elsewhere, its print is unpinned with a short fade.
//!
//! The captures and notes folders are watched too, but only for edits and
//! removals of pinned files: new files there are Tack's own. An edited note
//! (Notepad saving it) shows its new text. Also puts last session's prints
//! and notes back at startup.

use std::path::{Path, PathBuf};
use std::time::Duration;

use tack_core::{capture, files, note, thumbnail, Arrival, Kind, Origin, RevealReason};
use tack_windows::screenshots::{self, FolderEvent};
use tauri::AppHandle;

use crate::ipc::events::Removal;
use crate::prints::{self, Pin};
use crate::reveal;
use crate::state::lock;

/// How long a fresh file may stay undecodable while it is still being written.
const WRITE_PATIENCE: Duration = Duration::from_secs(3);
/// Editors often save by deleting and renaming a temp file into place; a
/// removal is only believed if the file is still gone a moment later.
const REMOVE_GRACE: Duration = Duration::from_millis(500);
/// Lets an editor finish writing before the print is redrawn.
const UPDATE_SETTLE: Duration = Duration::from_millis(250);

/// Watches `folder` for as long as the app runs.
pub fn watch(app: AppHandle, folder: PathBuf) {
    screenshots::watch(folder, move |event| match event {
        FolderEvent::Arrived(path) => arrived(&app, path),
        FolderEvent::Gone(path) => gone(&app, path),
        FolderEvent::Changed(path) => changed(&app, path),
    });
}

/// Pins a new image file and reveals the board.
fn arrived(app: &AppHandle, path: PathBuf) {
    if lock(app).board.find_path(&path).is_some() {
        // Saved over a pinned file (or a note).
        changed(app, path);
        return;
    }
    if !files::is_image(&path) {
        return;
    }
    if capture::in_captures(&path) {
        return;
    }
    let key = files::path_key(&path);
    if !lock(app).decoding.insert(key.clone()) {
        return;
    }
    let app = app.clone();
    std::thread::spawn(move || {
        let result = thumbnail::make_patiently(&path, WRITE_PATIENCE);
        lock(&app).decoding.remove(&key);
        match result {
            Ok(t) => {
                if let Pin::New(aged) = prints::pin(&app, path, t, Origin::Folder, prints::live(), None) {
                    reveal::reveal(&app, RevealReason::New);
                    // Queued behind the reveal on the main thread, so a print
                    // ageing out falls from a board that is already down.
                    let handle = app.clone();
                    let _ = app.run_on_main_thread(move || aged.announce(&handle));
                }
            }
            Err(e) => eprintln!("tack: cannot read {}: {e}", path.display()),
        }
    });
}

/// A pinned file disappeared: unpin it, unless it is back after the grace
/// period (an editor saving).
fn gone(app: &AppHandle, path: PathBuf) {
    if lock(app).board.find_path(&path).is_none() {
        return;
    }
    let app = app.clone();
    std::thread::spawn(move || {
        std::thread::sleep(REMOVE_GRACE);
        if path.exists() {
            refresh(&app, &path);
        } else {
            prints::remove_path(&app, &path, Removal::Quiet);
        }
    });
}

/// Written to: if it is pinned, redraw the print once the writing settles.
fn changed(app: &AppHandle, path: PathBuf) {
    let key = files::path_key(&path);
    {
        let mut s = lock(app);
        if s.board.find_path(&path).is_none() || !s.decoding.insert(key.clone()) {
            return;
        }
    }
    let app = app.clone();
    std::thread::spawn(move || {
        std::thread::sleep(UPDATE_SETTLE);
        lock(&app).decoding.remove(&key);
        refresh(&app, &path);
    });
}

fn refresh(app: &AppHandle, path: &Path) {
    let found = lock(app).board.find_path(path).map(|p| (p.stamp, p.kind));
    let Some((old, kind)) = found else { return };
    if old.is_some() && old == files::file_stamp(path) {
        return;
    }
    match kind {
        Kind::Note => {
            if let Ok(body) = note::read(path) {
                prints::update_note(app, path, body);
            }
        }
        Kind::Image => {
            if let Ok(t) = thumbnail::make_patiently(path, WRITE_PATIENCE) {
                prints::update_thumb(app, path, t);
            }
        }
    }
}

/// Re-pins last session's prints and notes as they were (kept or not, in
/// their old order), without animation. Prints whose files no longer exist are
/// dropped, and board.json is saved once so it stops listing them. Then
/// checks the history once, for anything that grew too old meanwhile.
pub fn restore(app: AppHandle) {
    std::thread::spawn(move || {
        let saved = lock(&app).take_restore();
        let mut gone = 0;
        for print in saved {
            if !print.path.is_file() {
                gone += 1;
                continue;
            }
            let arrival = Arrival::Restored { pinned_at: print.pinned_at, kept_at: print.kept_at };
            let pinned = match print.kind {
                Kind::Note => {
                    let Ok(body) = note::read(&print.path) else { continue };
                    prints::pin_note(&app, print.path, body, arrival)
                }
                Kind::Image => {
                    let Ok(t) = thumbnail::make(&print.path) else { continue };
                    prints::pin(&app, print.path, t, print.origin, arrival, None)
                }
            };
            if let Pin::New(aged) = pinned {
                aged.announce(&app);
            }
        }
        if gone > 0 {
            eprintln!("tack: {gone} pinned file(s) no longer exist; their prints are dropped");
            lock(&app).save();
        }
        prints::age_out(&app);
    });
}
