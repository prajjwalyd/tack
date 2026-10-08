//! Everything the app's threads share, behind one mutex: the board model, the
//! settings, the window and pointer state, and a little bookkeeping.
//!
//! Lock it with [`lock`] and keep the guard short: never hold it across a
//! window operation (those run on the UI thread, which may be waiting for the
//! lock itself), and send events once it is released.

use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::{Mutex, MutexGuard};
use std::time::SystemTime;

use tack_core::recycle::RecycleBin;
use tack_core::store::{self, Loaded, SavedPrint};
use tack_core::{capture, history, note, Board, Settings, View};
use tauri::{AppHandle, Manager};

pub type Shared = Mutex<AppState>;

pub struct AppState {
    pub board: Board,
    pub settings: Settings,
    pub view: View,
    /// The UI has asked for the prints. Before that, events would be lost,
    /// so nothing is emitted and the board is never revealed.
    pub ui_ready: bool,
    /// Files being decoded right now, so a burst of events decodes once.
    pub decoding: HashSet<String>,
    /// Prints saved last session, restored once at startup.
    restore: Vec<SavedPrint>,
    /// board.json could not be read and could not be backed up either, so
    /// this session must not write over it.
    hold_saves: bool,
}

impl AppState {
    /// Reads board.json and lets the history's old prints go. If the file
    /// was understood, the captures it no longer mentions (including those
    /// of aged-out prints), and the notes, then go to the Recycle Bin;
    /// otherwise none are touched, since any of them might still be on the
    /// board.
    pub fn load() -> AppState {
        let now = history::now_ms();
        let file = store::board_file();
        let (mut saved, loaded) = store::load(&file, now);
        saved.age_out(now);
        match &loaded {
            Loaded::Ok => {
                let paths: Vec<PathBuf> = saved.prints.iter().map(|p| p.path.clone()).collect();
                sweep_in_background(paths);
            }
            Loaded::Missing => {}
            Loaded::Unreadable { reason, backup: Some(backup) } => {
                eprintln!(
                    "tack: {} was not used ({reason}); a copy is at {}. Leftover captures are kept.",
                    file.display(),
                    backup.display()
                );
            }
            Loaded::Unreadable { reason, backup: None } => {
                eprintln!(
                    "tack: {} was not used ({reason}) and could not be copied; it will not be saved over this session",
                    file.display()
                );
            }
        }
        AppState {
            board: Board::new(),
            settings: saved.settings,
            view: View::default(),
            ui_ready: false,
            decoding: HashSet::new(),
            restore: saved.prints,
            hold_saves: loaded.must_not_save(),
        }
    }

    pub fn take_restore(&mut self) -> Vec<SavedPrint> {
        std::mem::take(&mut self.restore)
    }

    /// Writes the prints (in row order) and settings to board.json, unless
    /// that would overwrite a board.json nobody could back up.
    pub fn save(&self) {
        if !self.hold_saves {
            store::save(&store::board_file(), self.board.prints(), &self.settings);
        }
    }
}

/// Recycles leftover captures and notes off the startup path: the shell can
/// take a moment, and the board should not wait for it.
fn sweep_in_background(pinned: Vec<PathBuf>) {
    let spawned = std::thread::Builder::new().name("tack-sweep".into()).spawn(move || {
        let swept = capture::sweep(&capture::captures_folder(), &pinned, SystemTime::now(), &RecycleBin);
        if !swept.is_empty() {
            eprintln!("tack: moved {} leftover capture(s) to the Recycle Bin", swept.len());
        }
        let swept = note::sweep(&note::notes_folder(), &pinned, SystemTime::now(), &RecycleBin);
        if !swept.is_empty() {
            eprintln!("tack: moved {} leftover note(s) to the Recycle Bin", swept.len());
        }
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot start the leftover sweep: {e}");
    }
}

/// Locks the shared state. Never hold the guard across a window operation.
pub fn lock(app: &AppHandle) -> MutexGuard<'_, AppState> {
    let state = app.state::<Shared>();
    // A panic elsewhere must not take the whole board down with it.
    match state.inner().lock() {
        Ok(guard) => guard,
        Err(poisoned) => poisoned.into_inner(),
    }
}
