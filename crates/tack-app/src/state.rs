//! Everything the app's threads share, behind one mutex: the board model, the
//! settings, the window and pointer state, and a little bookkeeping.
//!
//! Lock it with [`lock`] and keep the guard short: never hold it across a
//! window operation (those run on the UI thread, which may be waiting for the
//! lock itself), and send events once it is released. Saving only takes a
//! snapshot under the lock; one thread writes board.json outside it.

use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::{Mutex, MutexGuard, OnceLock};
use std::time::{Duration, SystemTime};

use tack_core::recycle::RecycleBin;
use tack_core::store::{self, Loaded, SavedPrint, Saver, Snapshot};
use tack_core::{capture, history, note, Board, Settings, View};
use tauri::{AppHandle, Manager};

pub type Shared = Mutex<AppState>;

/// The least time between two writes of board.json: a burst of changes
/// (Clear, a restore) is written as its latest state, a few times at most.
const SAVE_GAP: Duration = Duration::from_millis(250);

pub struct AppState {
    pub board: Board,
    pub settings: Settings,
    pub view: View,
    /// The UI has asked for the prints. Before that, events would be lost,
    /// so nothing is emitted and the board is never revealed.
    pub ui_ready: bool,
    /// Files being decoded right now, so a burst of events decodes once.
    pub decoding: HashSet<String>,
    /// Prints saved last session and not on the board (yet): all of them
    /// until the restore is over, then those it could not read this time.
    /// Every save lists them, so none is forgotten.
    restore: Vec<SavedPrint>,
    /// board.json could not be read and could not be backed up either, so
    /// this session must not write over it.
    hold_saves: bool,
}

impl AppState {
    /// Reads board.json and lets the history's old prints go. If the file
    /// was understood, leftover captures and notes go to the Recycle Bin
    /// (see [`sweep_in_background`]); otherwise none are touched, since any
    /// of them might still be on the board.
    pub fn load() -> AppState {
        let now = history::now_ms();
        let file = store::board_file();
        // Before anything can save over it: when board.json last saved.
        let board_saved = std::fs::metadata(&file).and_then(|m| m.modified()).ok();
        let (mut saved, loaded) = store::load(&file, now);
        saved.age_out(now);
        match &loaded {
            Loaded::Ok => match store::mentioned(&file) {
                Some(mut keep) => {
                    keep.extend(saved.prints.iter().map(|p| p.path.clone()));
                    sweep_in_background(keep, capture::sweep_cutoff(SystemTime::now(), board_saved));
                }
                None => eprintln!("tack: board.json or a backup of it cannot be read; leftover files are kept"),
            },
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

    /// Last session's prints, to restore. A copy: they stay in every save
    /// until [`AppState::finish_restore`].
    pub fn to_restore(&self) -> Vec<SavedPrint> {
        self.restore.clone()
    }

    /// The restore is over. `retry` are the prints whose files could not be
    /// read this time (offline, locked): they stay listed in board.json for
    /// the next start rather than being dropped.
    pub fn finish_restore(&mut self, retry: Vec<SavedPrint>) {
        self.restore = retry;
    }

    /// Has the prints (in row order), the prints not restored, and the
    /// settings written to board.json shortly, off this lock, unless that
    /// would overwrite a board.json nobody could back up. Never waits for
    /// the disk.
    pub fn save(&self) {
        if !self.hold_saves {
            saver().queue(Snapshot::new(self.board.prints(), &self.restore, &self.settings));
        }
    }
}

fn saver() -> &'static Saver {
    static SAVER: OnceLock<Saver> = OnceLock::new();
    SAVER.get_or_init(|| Saver::start(store::board_file(), SAVE_GAP))
}

/// Writes the last save still waiting before returning: for Quit, so
/// nothing changed in the last moments is lost.
pub fn flush_saves() {
    saver().flush();
}

/// Recycles leftover captures and notes off the startup path: the shell can
/// take a moment, and the board should not wait for it. `keep` is every
/// path board.json or a backup of it mentions; only files last modified
/// before `cutoff` (8 days ago, and before board.json's last save) go.
fn sweep_in_background(keep: Vec<PathBuf>, cutoff: SystemTime) {
    let spawned = std::thread::Builder::new().name("tack-sweep".into()).spawn(move || {
        let swept = capture::sweep(&capture::captures_folder(), &keep, cutoff, &RecycleBin);
        if !swept.is_empty() {
            eprintln!("tack: moved {} leftover capture(s) to the Recycle Bin", swept.len());
        }
        let swept = note::sweep(&note::notes_folder(), &keep, cutoff, &RecycleBin);
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
