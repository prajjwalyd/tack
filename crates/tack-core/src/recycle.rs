//! Tack never destroys a file outright: when it cleans up one of its own
//! files (unpinned, aged out, left over from an earlier session), the file
//! goes to the Recycle Bin, where the user can still get it back. The bin
//! sits behind [`Recycler`] so the cleanup rules can be tested without it.

use std::path::Path;

/// Somewhere files go instead of being deleted for good.
pub trait Recycler {
    /// Moves the file at `path` there. On failure the file must be left
    /// exactly where it was.
    fn recycle(&self, path: &Path) -> Result<(), String>;
}

/// The Windows Recycle Bin.
#[derive(Clone, Copy, Debug, Default)]
pub struct RecycleBin;

impl Recycler for RecycleBin {
    fn recycle(&self, path: &Path) -> Result<(), String> {
        let path = path.to_path_buf();
        // `trash` sets COM up on whichever thread calls it and gives up hard
        // if that thread already chose another COM mode, as the UI thread
        // has. A short-lived thread of its own always starts clean.
        std::thread::spawn(move || trash::delete(&path).map_err(|e| e.to_string()))
            .join()
            .unwrap_or_else(|_| Err("the recycling thread stopped unexpectedly".into()))
    }
}

/// Recycles one file if it still exists. A failure is logged and the file
/// stays put: there is no fallback to deleting it. Returns whether it went.
pub(crate) fn recycle_file(path: &Path, bin: &dyn Recycler) -> bool {
    if !path.exists() {
        return false;
    }
    match bin.recycle(path) {
        Ok(()) => true,
        Err(e) => {
            eprintln!("tack: cannot move {} to the Recycle Bin, leaving it in place: {e}", path.display());
            false
        }
    }
}

#[cfg(test)]
pub(crate) mod testing {
    use std::cell::RefCell;
    use std::path::{Path, PathBuf};

    use super::Recycler;

    /// Stands in for the Recycle Bin in tests: notes each file it is handed
    /// and leaves the file on disk.
    #[derive(Default)]
    pub struct NotingBin {
        pub taken: RefCell<Vec<PathBuf>>,
    }

    impl Recycler for NotingBin {
        fn recycle(&self, path: &Path) -> Result<(), String> {
            self.taken.borrow_mut().push(path.to_path_buf());
            Ok(())
        }
    }

    /// A Recycle Bin that refuses everything.
    pub struct BrokenBin;

    impl Recycler for BrokenBin {
        fn recycle(&self, _: &Path) -> Result<(), String> {
            Err("no bin here".into())
        }
    }
}

#[cfg(test)]
mod tests {
    use super::testing::{BrokenBin, NotingBin};
    use super::*;

    #[test]
    fn a_missing_file_is_not_sent() {
        let bin = NotingBin::default();
        assert!(!recycle_file(Path::new(r"Z:\no\such\file.png"), &bin));
        assert!(bin.taken.borrow().is_empty());
    }

    #[test]
    fn a_refused_file_stays_where_it_is() {
        let dir = std::env::temp_dir().join(format!("tack-recycle-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("capture.png");
        std::fs::write(&file, b"x").unwrap();
        let went = recycle_file(&file, &BrokenBin);
        let still_there = file.exists();
        let _ = std::fs::remove_dir_all(&dir);
        assert!(!went);
        assert!(still_there, "a failed recycle never deletes the file instead");
    }
}
