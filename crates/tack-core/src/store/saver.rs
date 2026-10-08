//! Writing board.json off the app's lock. A save hands a [`Snapshot`] to one
//! thread, which writes the latest it has and then rests a moment, so a
//! burst of changes (Clear unpinning fifty prints) costs a few writes, and
//! nothing waits on the disk while holding the board. A write that fails is
//! tried again a little later unless a newer snapshot has replaced it.
//! [`Saver::flush`] writes whatever is still waiting, on the caller's
//! thread, for Quit.

use std::path::PathBuf;
#[cfg(test)]
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Condvar, Mutex, MutexGuard};
use std::time::Duration;

use super::{write, Snapshot};

/// How long a failed write waits before it is tried again: long enough not
/// to fill the log while the disk is full, short enough to outlast a
/// virus scanner holding board.json for a moment.
const RETRY: Duration = Duration::from_secs(5);

/// Writes board.json snapshots on a thread of its own.
pub struct Saver {
    shared: Arc<Shared>,
    /// The thread runs; if it could not start, saves are written at once.
    threaded: bool,
}

struct Shared {
    file: PathBuf,
    /// The latest snapshot not written yet.
    next: Mutex<Option<Snapshot>>,
    queued: Condvar,
    /// Held for each write, so two never race over board.json.tmp, and a
    /// flush waits for a write already under way.
    writing: Mutex<()>,
    #[cfg(test)]
    writes: AtomicUsize,
}

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

impl Saver {
    /// Starts the thread that writes `file`, at most once per `gap`. If it
    /// cannot start, each save is written on the caller's thread instead.
    pub fn start(file: PathBuf, gap: Duration) -> Saver {
        let shared = Arc::new(Shared {
            file,
            next: Mutex::new(None),
            queued: Condvar::new(),
            writing: Mutex::new(()),
            #[cfg(test)]
            writes: AtomicUsize::new(0),
        });
        let worker = shared.clone();
        let spawned = std::thread::Builder::new().name("tack-save-board".into()).spawn(move || worker.run(gap));
        if let Err(e) = &spawned {
            eprintln!("tack: cannot start the board saver, saving as it goes instead: {e}");
        }
        Saver { shared, threaded: spawned.is_ok() }
    }

    /// Has `snapshot` written soon, in place of any older one still waiting.
    /// Never waits for the disk (unless the thread could not start).
    pub fn queue(&self, snapshot: Snapshot) {
        *lock(&self.shared.next) = Some(snapshot);
        if self.threaded {
            self.shared.queued.notify_one();
        } else {
            self.flush();
        }
    }

    /// Writes the snapshot still waiting, if any, before returning; waits
    /// for a write under way first.
    pub fn flush(&self) {
        self.shared.write_waiting();
    }
}

impl Shared {
    fn run(&self, gap: Duration) {
        loop {
            {
                let mut next = lock(&self.next);
                while next.is_none() {
                    next = self.queued.wait(next).unwrap_or_else(|poisoned| poisoned.into_inner());
                }
            }
            let rest = if self.write_waiting() { gap } else { RETRY };
            std::thread::sleep(rest);
        }
    }

    /// Writes the waiting snapshot. False if that failed; it then waits
    /// again, unless something newer took its place meanwhile.
    fn write_waiting(&self) -> bool {
        let _writing = lock(&self.writing);
        let Some(snapshot) = lock(&self.next).take() else { return true };
        #[cfg(test)]
        self.writes.fetch_add(1, Ordering::SeqCst);
        match write(&self.file, &snapshot) {
            Ok(()) => true,
            Err(e) => {
                eprintln!("tack: cannot save {}, trying again shortly: {e}", self.file.display());
                lock(&self.next).get_or_insert(snapshot);
                false
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::print::{Kind, Origin};
    use crate::settings::Settings;
    use crate::store::{load, SavedPrint};

    fn snapshot(n: u64) -> Snapshot {
        let print = SavedPrint {
            path: PathBuf::from(format!(r"C:\nowhere\{n}.png")),
            kind: Kind::Image,
            origin: Origin::Folder,
            pinned_at: n,
            kept_at: Some(n),
        };
        Snapshot::new(&[], &[print], &Settings::default())
    }

    #[test]
    fn a_burst_of_saves_writes_the_latest_a_few_times_at_most() {
        let dir = std::env::temp_dir().join(format!("tack-saver-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let file = dir.join("board.json");
        let saver = Saver::start(file.clone(), Duration::from_millis(300));
        for n in 1..=50 {
            saver.queue(snapshot(n));
        }
        saver.flush();
        let writes = saver.shared.writes.load(Ordering::SeqCst);
        let (saved, _) = load(&file, 0);
        let _ = std::fs::remove_dir_all(&dir);
        assert!((1..=3).contains(&writes), "{writes} writes for 50 saves");
        assert_eq!(saved.prints.len(), 1);
        assert_eq!(saved.prints[0].pinned_at, 50, "the last snapshot is the one on disk");
    }

    #[test]
    fn a_failed_write_waits_to_be_tried_again() {
        let dir = std::env::temp_dir().join(format!("tack-saver-retry-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        // A folder where the file should be: every write fails.
        let file = dir.join("board.json");
        std::fs::create_dir_all(&file).unwrap();
        let saver = Saver { shared: Arc::new(shared(file.clone())), threaded: false };
        saver.queue(snapshot(7));
        let kept = lock(&saver.shared.next).is_some();
        // Once the way is clear, the same snapshot goes through.
        std::fs::remove_dir(&file).unwrap();
        saver.flush();
        let (saved, _) = load(&file, 0);
        let _ = std::fs::remove_dir_all(&dir);
        assert!(kept, "the snapshot is not lost when its write fails");
        assert_eq!(saved.prints[0].pinned_at, 7);
    }

    fn shared(file: PathBuf) -> Shared {
        Shared {
            file,
            next: Mutex::new(None),
            queued: Condvar::new(),
            writing: Mutex::new(()),
            writes: AtomicUsize::new(0),
        }
    }
}
