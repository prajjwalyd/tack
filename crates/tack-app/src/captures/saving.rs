//! Writing a capture's PNG in the background. The print is pinned under the
//! file's reserved name while the file is still being written; anything
//! that needs the file itself (copy, open, drag, recycle...) waits for the
//! write through [`when_saved`] or [`wait_saved`].

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::mpsc::Sender;
use std::sync::{Arc, Condvar, Mutex, MutexGuard, OnceLock};
use std::time::{Duration, Instant};

use image::DynamicImage;
use tack_core::capture;
use tack_windows::snipping_tool;
use tauri::AppHandle;

use super::worker;
use crate::ipc::events::Removal;
use crate::prints;
use crate::state::lock;

/// The longest anything waits for a capture's file to be written.
const SAVE_PATIENCE: Duration = Duration::from_secs(5);

/// A capture to write to its reserved path.
type SaveJob = (PathBuf, Arc<DynamicImage>);

static SAVES: OnceLock<Sender<SaveJob>> = OnceLock::new();

/// Captures whose file is still being written, with what to do with each
/// once it is.
type Waiting = HashMap<PathBuf, Vec<Box<dyn FnOnce(&Path) + Send>>>;

static SAVING: Mutex<Option<Waiting>> = Mutex::new(None);
static SAVED: Condvar = Condvar::new();

fn saving() -> MutexGuard<'static, Option<Waiting>> {
    SAVING.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Starts the save thread (once).
pub(super) fn start_worker(app: &AppHandle) {
    let app = app.clone();
    SAVES.get_or_init(move || {
        worker("tack-save", move |(path, img): SaveJob| {
            #[cfg(debug_assertions)]
            let started = Instant::now();
            let result = capture::write_reserved(&img, &path);
            drop(img);
            trace!("capture: file written in {:.0} ms", started.elapsed().as_secs_f64() * 1000.0);
            finish_save(&app, &path, result);
        })
    });
}

/// Claims the capture's file name and has the save thread write it.
/// Returns the path it will have.
pub(super) fn save_in_background(img: Arc<DynamicImage>) -> std::io::Result<PathBuf> {
    let path = capture::reserve(&capture::captures_folder(), snipping_tool::local_time())?;
    saving().get_or_insert_with(HashMap::new).insert(path.clone(), Vec::new());
    let sent = SAVES.get().is_some_and(|saves| saves.send((path.clone(), img)).is_ok());
    if !sent {
        saving().as_mut().map(|w| w.remove(&path));
        let _ = std::fs::remove_file(capture::partial_path(&path));
        return Err(std::io::Error::other("the save thread is not running"));
    }
    Ok(path)
}

fn finish_save(app: &AppHandle, path: &Path, result: std::io::Result<()>) {
    match result {
        // From now on only an edit of the file counts as a change.
        Ok(()) => {
            lock(app).board.refresh_stamp(path);
        }
        Err(e) => {
            eprintln!("tack: cannot save the capture {}: {e}", path.display());
            prints::remove_path(app, path, Removal::Quiet);
        }
    }
    let waiting = saving().as_mut().and_then(|w| w.remove(path)).unwrap_or_default();
    SAVED.notify_all();
    for then in waiting {
        then(path);
    }
}

/// Runs `then` with the file once it is written: straight away for any file
/// that is not a capture still being saved, else on the save thread when it
/// is done. Never blocks.
pub fn when_saved(path: &Path, then: impl FnOnce(&Path) + Send + 'static) {
    {
        let mut saving = saving();
        if let Some(waiting) = saving.as_mut().and_then(|w| w.get_mut(path)) {
            waiting.push(Box::new(then));
            return;
        }
    }
    then(path);
}

/// The file is a capture still being written.
pub fn is_saving(path: &Path) -> bool {
    saving().as_ref().is_some_and(|w| w.contains_key(path))
}

/// Blocks until the file is written, if it is a capture still being saved
/// (for at most [`SAVE_PATIENCE`]). Off the UI thread only.
pub fn wait_saved(path: &Path) {
    let give_up = Instant::now() + SAVE_PATIENCE;
    let mut saving = saving();
    while saving.as_ref().is_some_and(|w| w.contains_key(path)) {
        let left = give_up.saturating_duration_since(Instant::now());
        if left.is_zero() {
            return;
        }
        saving = SAVED.wait_timeout(saving, left).unwrap_or_else(|p| p.into_inner()).0;
    }
}
