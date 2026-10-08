//! Screenshot files: what a change in the Screenshots folder means for the
//! board. New image files get pinned and the board peeks out; an edit to a
//! pinned file refreshes its thumbnail; if a pinned file is deleted or moved
//! elsewhere, its print is unpinned with a short fade.
//!
//! The captures and notes folders are watched too, but only for edits and
//! removals of pinned files: new files there are Tack's own. An edited note
//! (Notepad saving it) shows its new text. Also puts last session's prints
//! and notes back at startup.

use std::io::ErrorKind;
use std::path::{Path, PathBuf};
use std::time::Duration;

use tack_core::store::SavedPrint;
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
    let handle = app.clone();
    let done = key.clone();
    let started = spawn("tack-decode", move || {
        let app = handle;
        let result = thumbnail::make_patiently(&path, WRITE_PATIENCE);
        lock(&app).decoding.remove(&done);
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
    if !started {
        lock(app).decoding.remove(&key);
    }
}

/// A pinned file disappeared: unpin it, unless it is back after the grace
/// period (an editor saving).
fn gone(app: &AppHandle, path: PathBuf) {
    if lock(app).board.find_path(&path).is_none() {
        return;
    }
    let app = app.clone();
    spawn("tack-gone", move || {
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
    let handle = app.clone();
    let done = key.clone();
    let started = spawn("tack-refresh", move || {
        std::thread::sleep(UPDATE_SETTLE);
        lock(&handle).decoding.remove(&done);
        refresh(&handle, &path);
    });
    if !started {
        lock(app).decoding.remove(&key);
    }
}

/// Runs `job` on a short-lived thread of its own. If no thread can be
/// started (the process is out of them), the event is logged and skipped
/// rather than taking the watcher down. Returns whether it started.
fn spawn(name: &str, job: impl FnOnce() + Send + 'static) -> bool {
    match std::thread::Builder::new().name(name.into()).spawn(job) {
        Ok(_) => true,
        Err(e) => {
            eprintln!("tack: cannot start {name}, skipping this file event: {e}");
            false
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
    let started = spawn("tack-restore", move || {
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
    if !started {
        eprintln!("tack: last session's prints stay in board.json for the next start");
    }
}

/// An entry Tack itself could have written: a picture for a print, a text
/// file directly in the notes folder for a note.
fn restorable(print: &SavedPrint) -> bool {
    match print.kind {
        Kind::Image => files::is_image(&print.path),
        Kind::Note => note::in_notes(&print.path),
    }
}
