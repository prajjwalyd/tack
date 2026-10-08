//! What can happen to a print, from the app's side: each change is made on
//! the board model, saved, told to the UI (once it is ready) and, for a
//! capture leaving the board, followed by moving its file to the Recycle Bin.
//!
//! Also the history's upkeep: unkept prints age out once they are a week old
//! (checked after the restore at startup, then every hour) or when fifty
//! newer ones push them out.

use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use tack_core::recycle::RecycleBin;
use tack_core::{capture, history, Arrival, Origin, PinOutcome, Print, Thumb};
use tack_windows::shell;
use tauri::AppHandle;

use crate::ipc::events::{self, Removal};
use crate::state::{lock, AppState};

/// How often the history is checked for prints that have grown too old.
const AGEING_INTERVAL: Duration = Duration::from_secs(60 * 60);

/// How a pin went.
pub enum Pin {
    /// A new print. The prints that aged out to make room are left to the
    /// caller to announce, see [`Aged::announce`].
    New(Aged),
    /// A restored print already past the history's limits; not pinned.
    Expired,
    /// The screenshot file of a capture pinned moments ago: that print now
    /// shows the file and the capture's own copy is gone.
    Merged,
    /// A capture of a screenshot file pinned moments ago; nothing was pinned.
    Duplicate,
    /// Already on the board.
    Exists,
}

/// Prints that aged out of the history, already off the board and with
/// their capture files sent to the Recycle Bin; the UI has not heard yet.
pub struct Aged {
    ids: Vec<String>,
}

impl Aged {
    /// Tells the UI. The caller picks the moment: after the reveal for a
    /// new screenshot, so the old prints fall from a board that is down. If
    /// the board is tucked by then nobody is watching, and they go quietly.
    pub fn announce(self, app: &AppHandle) {
        if self.ids.is_empty() {
            return;
        }
        let (ready, how) = {
            let s = lock(app);
            (s.ui_ready, removal_now(&s))
        };
        if ready {
            for id in self.ids {
                events::print_removed(app, &id, how);
            }
        }
    }
}

/// How a print leaving on its own is shown: falling if anyone can see the
/// board, a quiet fade otherwise.
fn removal_now(s: &AppState) -> Removal {
    if s.view.shown {
        Removal::Fall
    } else {
        Removal::Quiet
    }
}

/// Sends the files of captures that left the board to the Recycle Bin;
/// screenshot files stay.
fn discard_files(prints: &[Print]) {
    let paths = prints.iter().filter(|p| p.origin == Origin::Capture).map(|p| p.path.clone()).collect();
    discard_captures(paths);
}

/// Sends capture files Tack no longer needs to the Recycle Bin, never
/// deleting them outright. Runs on a thread of its own, since the shell can
/// take a moment; a file that cannot be recycled is logged and left alone.
pub fn discard_captures(paths: Vec<PathBuf>) {
    if paths.is_empty() {
        return;
    }
    let spawned = std::thread::Builder::new().name("tack-recycle".into()).spawn(move || {
        let folder = capture::captures_folder();
        for path in &paths {
            capture::discard_path(path, &folder, &RecycleBin);
        }
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot start recycling, the files stay: {e}");
    }
}

/// The file behind a print.
pub fn path_of(app: &AppHandle, id: &str) -> Option<PathBuf> {
    lock(app).board.find(id).map(|p| p.path.clone())
}

/// Pins a screenshot. A live one has just arrived: it animates in, joins the
/// front of the history and may turn out to be the other half of an
/// auto-saved screenshot. A restored one comes back as it was saved.
pub fn pin(app: &AppHandle, path: PathBuf, thumb: Thumb, origin: Origin, arrival: Arrival) -> Pin {
    let mut s = lock(app);
    match s.board.pin(path, thumb, origin, arrival, history::now_ms()) {
        PinOutcome::Exists => Pin::Exists,
        PinOutcome::Duplicate => Pin::Duplicate,
        PinOutcome::Merged { print, capture } => {
            s.save();
            let ready = s.ui_ready;
            drop(s);
            if ready {
                events::print_updated(app, &print);
            }
            // The user's own file now stands in for Tack's copy.
            discard_captures(vec![capture]);
            Pin::Merged
        }
        PinOutcome::New { print, aged } => {
            s.save();
            let (ready, order) = (s.ui_ready, s.board.order());
            drop(s);
            if ready {
                events::print_added(app, &print, matches!(arrival, Arrival::Live(_)));
                events::order_changed(app, &order);
                trace!("print {} added: board:print-added and board:order-changed sent", print.id);
            }
            discard_files(&aged);
            Pin::New(Aged { ids: aged.into_iter().map(|p| p.id).collect() })
        }
        PinOutcome::Expired { print, aged } => {
            if !aged.is_empty() {
                s.save();
            }
            drop(s);
            discard_files(std::slice::from_ref(&print));
            discard_files(&aged);
            Aged { ids: aged.into_iter().map(|p| p.id).collect() }.announce(app);
            Pin::Expired
        }
    }
}

/// Keeps a print, or stops keeping it. The UI hears the print's new state,
/// then the new order if it moved. A print no longer kept may be past the
/// history's limits and age out at once.
pub fn set_kept(app: &AppHandle, id: &str, kept: bool) {
    let (change, ready, order, how) = {
        let mut s = lock(app);
        let Some(change) = s.board.set_kept(id, kept, history::now_ms()) else { return };
        s.save();
        (change, s.ui_ready, s.board.order(), removal_now(&s))
    };
    discard_files(&change.aged);
    if !ready {
        return;
    }
    events::print_updated(app, &change.print);
    for print in &change.aged {
        events::print_removed(app, &print.id, how);
    }
    if change.moved {
        events::order_changed(app, &order);
    }
}

/// Lets go of the unkept prints that have grown too old.
pub fn age_out(app: &AppHandle) {
    let (aged, ready, how) = {
        let mut s = lock(app);
        let aged = s.board.age_out(history::now_ms());
        if aged.is_empty() {
            return;
        }
        s.save();
        (aged, s.ui_ready, removal_now(&s))
    };
    discard_files(&aged);
    if ready {
        for print in &aged {
            events::print_removed(app, &print.id, how);
        }
    }
}

/// Checks the history every hour for as long as the app runs.
pub fn age_out_hourly(app: AppHandle) {
    let spawned = std::thread::Builder::new().name("tack-history".into()).spawn(move || loop {
        std::thread::sleep(AGEING_INTERVAL);
        age_out(&app);
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot start the history timer: {e}");
    }
}

/// Unpins a print and, if it was a capture, recycles its file.
pub fn remove(app: &AppHandle, id: &str, how: Removal) -> Option<Print> {
    let print = unpin(app, id, how)?;
    discard_files(std::slice::from_ref(&print));
    Some(print)
}

/// Unpins a print and leaves its file alone.
pub fn unpin(app: &AppHandle, id: &str, how: Removal) -> Option<Print> {
    let (print, ready) = {
        let mut s = lock(app);
        let print = s.board.remove(id)?;
        s.save();
        (print, s.ui_ready)
    };
    if ready {
        events::print_removed(app, &print.id, how);
    }
    Some(print)
}

/// The file is gone already (deleted, or moved by a drop into a folder).
pub fn remove_path(app: &AppHandle, path: &Path, how: Removal) {
    let id = lock(app).board.find_path(path).map(|p| p.id.clone());
    if let Some(id) = id {
        unpin(app, &id, how);
    }
}

/// Unpins every print that is not kept, one after another so the pins pop
/// out in a ripple. Kept prints were kept on purpose; only Unpin removes them.
pub fn clear(app: &AppHandle) {
    let ids: Vec<String> = lock(app).board.prints().iter().filter(|p| !p.kept).map(|p| p.id.clone()).collect();
    let app = app.clone();
    std::thread::spawn(move || {
        for (n, id) in ids.iter().enumerate() {
            if n > 0 {
                std::thread::sleep(Duration::from_millis(45));
            }
            remove(&app, id, Removal::Fall);
        }
    });
}

/// Points a print at its file's new place. Returns the old path, so a move
/// that fails can be undone.
pub fn repoint(app: &AppHandle, id: &str, path: PathBuf, origin: Origin) -> Option<PathBuf> {
    let (print, old, ready) = {
        let mut s = lock(app);
        let (print, old) = s.board.repoint(id, path, origin)?;
        s.save();
        (print, old, s.ui_ready)
    };
    if ready {
        events::print_updated(app, &print);
    }
    Some(old)
}

/// Redraws a print whose file was edited.
pub fn update_thumb(app: &AppHandle, path: &Path, thumb: Thumb) {
    let (print, ready) = {
        let mut s = lock(app);
        let Some(print) = s.board.update_thumb(path, thumb) else { return };
        (print, s.ui_ready)
    };
    if ready {
        events::print_updated(app, &print);
    }
}

/// Puts the print's full image on the clipboard; the UI shows "Copied".
pub fn copy(app: &AppHandle, id: &str) -> Result<(), String> {
    let path = path_of(app, id).ok_or("no such print")?;
    shell::copy_image(&path)?;
    events::print_copied(app, id);
    Ok(())
}

/// Unpins first, so the print gets the full pop-out animation instead of the
/// short fade the watcher would use for a vanished file. A capture is
/// recycled too, not deleted.
pub fn recycle(app: &AppHandle, id: &str) {
    let Some(print) = unpin(app, id, Removal::Fall) else { return };
    shell::recycle(print.path);
}

/// A fresh screenshot, arriving now.
pub fn live() -> Arrival {
    Arrival::Live(Instant::now())
}
