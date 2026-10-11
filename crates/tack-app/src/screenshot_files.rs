//! Screenshot files: what a change in the Screenshots folder means for the
//! board. New image files get pinned and the board peeks out; an edit to a
//! pinned file refreshes its thumbnail; if a pinned file is deleted or moved
//! elsewhere, its print is unpinned with a short fade.
//!
//! Tack's own files (captures, notes) have no watcher: each reveal looks them
//! over instead ([`check_owned`]), so an edited note (Notepad saving it)
//! shows its new text and a deleted one goes. Also puts last session's
//! prints and notes back at startup.

use std::io::ErrorKind;
use std::path::{Path, PathBuf};
use std::time::Duration;

use tack_core::store::SavedPrint;
use tack_core::{files, note, thumbnail, Arrival, Kind, Origin, RevealReason};
use tack_windows::screenshots::{self, FolderEvent};
use tauri::AppHandle;

use crate::captures;
use crate::ipc::events::Removal;
use crate::prints::{self, Pin};
use crate::reveal::{self, after};
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
    let key = files::path_key(&path);
    if !lock(app).decoding.insert(key.clone()) {
        return;
    }
    let app = app.clone();
    tauri::async_runtime::spawn_blocking(move || {
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
    after(REMOVE_GRACE, move || {
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
    let handle = app.clone();
    after(UPDATE_SETTLE, move || {
        lock(&handle).decoding.remove(&key);
        refresh(&handle, &path);
    });
}

/// Looks Tack's own files over, on the calling (worker) thread: a pinned
/// capture or note that was edited or deleted elsewhere is treated as the
/// watcher would have. A capture still being written is left alone.
pub fn check_owned(app: &AppHandle) {
    let owned: Vec<_> =
        lock(app).board.prints().iter().filter(|p| p.owned()).map(|p| (p.path.clone(), p.stamp)).collect();
    for (path, old) in owned {
        if captures::is_saving(&path) {
            continue;
        }
        match files::file_stamp(&path) {
            None if old.is_some() => gone(app, path),
            now if now != old => changed(app, path),
            _ => {}
        }
    }
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
/// their old order), without animation, then saves board.json once.
/// Until then every save still lists the prints not put back yet, so a
/// quit or crash halfway forgets none of them. Prints whose files are gone
/// are dropped, and so are entries no Tack wrote (an image that is not one,
/// a note outside the notes folder: board.json was edited by hand). A file
/// that is there but cannot be read now (offline, locked) stays listed for
/// the next start. Then checks the history once, for anything that grew
/// too old meanwhile.
pub fn restore(app: AppHandle) {
    tauri::async_runtime::spawn_blocking(move || {
        let saved = lock(&app).to_restore();
        if saved.is_empty() {
            prints::age_out(&app);
            return;
        }
        let mut retry = Vec::new();
        let (mut gone, mut invalid) = (0, 0);
        for print in saved {
            if !restorable(&print) {
                invalid += 1;
                continue;
            }
            match std::fs::metadata(&print.path) {
                Err(e) if e.kind() == ErrorKind::NotFound => {
                    gone += 1;
                    continue;
                }
                Ok(meta) if !meta.is_file() => {
                    invalid += 1;
                    continue;
                }
                // Anything else (no access, a drive not ready) may pass: try.
                _ => {}
            }
            let arrival = Arrival::Restored { pinned_at: print.pinned_at, kept_at: print.kept_at };
            let pinned = match print.kind {
                Kind::Note => {
                    note::read(&print.path).map(|body| prints::pin_note(&app, print.path.clone(), body, arrival))
                }
                Kind::Image => thumbnail::make(&print.path)
                    .map(|t| prints::pin(&app, print.path.clone(), t, print.origin, arrival, None)),
            };
            match pinned {
                Ok(Pin::New(aged)) => aged.announce(&app),
                Ok(_) => {}
                Err(e) => {
                    eprintln!("tack: cannot read {} now, keeping it for next time: {e}", print.path.display());
                    retry.push(print);
                }
            }
        }
        if gone > 0 {
            eprintln!("tack: {gone} pinned file(s) no longer exist; their prints are dropped");
        }
        if invalid > 0 {
            eprintln!("tack: {invalid} print(s) in board.json are not files Tack pins; they are dropped");
        }
        {
            let mut s = lock(&app);
            s.finish_restore(retry);
            s.save();
        }
        prints::age_out(&app);
    });
}

/// An entry Tack itself could have written: a picture for a print, a text
/// file directly in the notes folder for a note.
fn restorable(print: &SavedPrint) -> bool {
    match print.kind {
        Kind::Image => files::is_image(&print.path),
        Kind::Note => note::in_notes(&print.path),
    }
}
