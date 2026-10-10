//! A watcher for one folder's changes, on `ReadDirectoryChangesW`. Its thread
//! sleeps in the kernel until something changes or the watcher is dropped:
//! no timer and no polling, so 0 wake-ups per second while the folder is
//! quiet. (The `notify` crate it replaces woke every 100 ms per watcher.)

use std::ffi::OsString;
use std::fs::{File, OpenOptions};
use std::io;
use std::os::windows::ffi::OsStringExt;
use std::os::windows::fs::OpenOptionsExt;
use std::os::windows::io::{AsRawHandle, FromRawHandle, OwnedHandle};
use std::path::{Path, PathBuf};
use std::sync::mpsc::{self, Sender};
use std::sync::Arc;
use std::thread::JoinHandle;

use windows::core::HRESULT;
use windows::Win32::Foundation::{ERROR_NOTIFY_ENUM_DIR, HANDLE, WAIT_OBJECT_0};
use windows::Win32::Storage::FileSystem::{
    ReadDirectoryChangesW, FILE_ACTION_ADDED, FILE_ACTION_MODIFIED, FILE_ACTION_REMOVED, FILE_ACTION_RENAMED_NEW_NAME,
    FILE_ACTION_RENAMED_OLD_NAME, FILE_FLAG_BACKUP_SEMANTICS, FILE_FLAG_OVERLAPPED, FILE_LIST_DIRECTORY,
    FILE_NOTIFY_CHANGE_CREATION, FILE_NOTIFY_CHANGE_DIR_NAME, FILE_NOTIFY_CHANGE_FILE_NAME,
    FILE_NOTIFY_CHANGE_LAST_WRITE, FILE_NOTIFY_CHANGE_SIZE, FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE,
};
use windows::Win32::System::Threading::{CreateEventW, SetEvent, WaitForMultipleObjects, INFINITE};
use windows::Win32::System::IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED};

/// What the kernel reports per entry. Network shares take at most 64 KiB.
const BUFFER: usize = 32 * 1024;

/// What happened to a name in the folder.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub(crate) enum Change {
    Added,
    Removed,
    Modified,
    /// The old name of a renamed entry.
    RenamedFrom,
    /// The new name of a renamed entry.
    RenamedTo,
}

/// Watches one folder (not its subfolders) until dropped.
pub(crate) struct DirWatcher {
    stop: Arc<OwnedHandle>,
    thread: Option<JoinHandle<()>>,
}

impl DirWatcher {
    /// Starts watching `dir`, calling `on_change` on the watcher's thread with
    /// the full path of each entry that changed. The thread ends if the folder
    /// is deleted (or its volume goes away); more changes than the buffer holds
    /// are dropped, as the callers only act on single files.
    pub(crate) fn start(dir: &Path, on_change: impl FnMut(Change, PathBuf) + Send + 'static) -> io::Result<DirWatcher> {
        let handle = OpenOptions::new()
            .access_mode(FILE_LIST_DIRECTORY.0)
            .share_mode((FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE).0)
            .custom_flags((FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED).0)
            .open(dir)?;
        let done = event()?;
        let stop = Arc::new(event()?);
        let (dir, signal) = (dir.to_path_buf(), stop.clone());
        let (ready, requested) = mpsc::channel();
        let thread = std::thread::Builder::new()
            .name("tack-dir-watch".into())
            .spawn(move || run(&dir, &handle, &done, &signal, ready, on_change))?;
        // Changes made once this returns must not slip by before the first request is in.
        let _ = requested.recv();
        Ok(DirWatcher { stop, thread: Some(thread) })
    }
}

impl Drop for DirWatcher {
    fn drop(&mut self) {
        unsafe {
            let _ = SetEvent(raw(&*self.stop));
        }
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

/// A manual-reset event, not signalled.
fn event() -> io::Result<OwnedHandle> {
    let event = unsafe { CreateEventW(None, true, false, None) }?;
    Ok(unsafe { OwnedHandle::from_raw_handle(event.0) })
}

fn raw(handle: &impl AsRawHandle) -> HANDLE {
    HANDLE(handle.as_raw_handle())
}

/// The watcher thread: one request at a time, asleep until it completes or
/// `stop` is set. Holds no lock while `on_change` runs.
fn run(
    dir: &Path,
    handle: &File,
    done: &OwnedHandle,
    stop: &OwnedHandle,
    ready: Sender<()>,
    mut on_change: impl FnMut(Change, PathBuf),
) {
    let filter = FILE_NOTIFY_CHANGE_FILE_NAME
        | FILE_NOTIFY_CHANGE_DIR_NAME
        | FILE_NOTIFY_CHANGE_SIZE
        | FILE_NOTIFY_CHANGE_LAST_WRITE
        | FILE_NOTIFY_CHANGE_CREATION;
    // The kernel wants the buffer aligned to a DWORD.
    #[repr(align(4))]
    struct Buffer([u8; BUFFER]);
    let mut buffer = Box::new(Buffer([0; BUFFER]));
    let mut request = OVERLAPPED { hEvent: raw(done), ..Default::default() };
    let (dir_handle, mut bytes) = (raw(handle), 0u32);
    let mut ready = Some(ready);
    loop {
        // Issuing a request also resets `done`.
        let issued = unsafe {
            ReadDirectoryChangesW(
                dir_handle,
                buffer.0.as_mut_ptr().cast(),
                BUFFER as u32,
                false,
                filter,
                None,
                Some(&mut request),
                None,
            )
        };
        if let Err(e) = issued {
            eprintln!("tack: cannot watch {}: {e}", dir.display());
            return;
        }
        // Dropping the sender tells `start` the first request is in.
        ready.take();
        let woken = unsafe { WaitForMultipleObjects(&[raw(done), raw(stop)], false, INFINITE) };
        if woken != WAIT_OBJECT_0 {
            // Withdraw the request before the buffer it fills goes away.
            unsafe {
                let _ = CancelIoEx(dir_handle, Some(&request));
                let _ = GetOverlappedResult(dir_handle, &request, &mut bytes, true);
            }
            return;
        }
        match unsafe { GetOverlappedResult(dir_handle, &request, &mut bytes, false) } {
            Ok(()) => {}
            // Network shares report an overflow as an error.
            Err(e) if e.code() == HRESULT::from_win32(ERROR_NOTIFY_ENUM_DIR.0) => continue,
            Err(e) => {
                eprintln!("tack: stopped watching {}: {e}", dir.display());
                return;
            }
        }
        // No bytes: an overflow, too.
        for (action, name) in entries(&buffer.0[..bytes as usize]) {
            if let Some(change) = change(action) {
                on_change(change, dir.join(name));
            }
        }
    }
}

fn change(action: u32) -> Option<Change> {
    match action {
        a if a == FILE_ACTION_ADDED.0 => Some(Change::Added),
        a if a == FILE_ACTION_REMOVED.0 => Some(Change::Removed),
        a if a == FILE_ACTION_MODIFIED.0 => Some(Change::Modified),
        a if a == FILE_ACTION_RENAMED_OLD_NAME.0 => Some(Change::RenamedFrom),
        a if a == FILE_ACTION_RENAMED_NEW_NAME.0 => Some(Change::RenamedTo),
        _ => None,
    }
}

/// Splits the filled part of the buffer into its `FILE_NOTIFY_INFORMATION`
/// records: (action, name). Stops at a record that does not fit.
fn entries(buf: &[u8]) -> impl Iterator<Item = (u32, OsString)> + '_ {
    let u32_at = |at: usize| Some(u32::from_le_bytes(buf.get(at..at + 4)?.try_into().ok()?));
    let mut at = Some(0);
    std::iter::from_fn(move || {
        let start = at?;
        let (next, action, len) = (u32_at(start)?, u32_at(start + 4)?, u32_at(start + 8)? as usize);
        let name = buf.get(start + 12..start + 12 + len)?;
        at = (next != 0).then_some(start + next as usize);
        let name: Vec<u16> = name.as_chunks().0.iter().map(|&c| u16::from_le_bytes(c)).collect();
        Some((action, OsString::from_wide(&name)))
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashSet;
    use std::sync::mpsc::{self, Receiver};
    use std::time::{Duration, Instant};

    /// A fresh folder in the temp dir, removed on drop.
    struct Scratch(PathBuf);

    impl Scratch {
        fn new(name: &str) -> Scratch {
            let dir = std::env::temp_dir().join(format!("tack-dir-watch-{}-{name}", std::process::id()));
            let _ = std::fs::remove_dir_all(&dir);
            std::fs::create_dir_all(&dir).unwrap();
            Scratch(dir)
        }
    }

    impl Drop for Scratch {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    fn watch(dir: &Path) -> (DirWatcher, Receiver<(Change, PathBuf)>) {
        let (tx, rx) = mpsc::channel();
        let watcher = DirWatcher::start(dir, move |change, path| {
            let _ = tx.send((change, path));
        })
        .unwrap();
        (watcher, rx)
    }

    /// Waits for `change` on `path`, skipping whatever else is reported.
    fn expect(rx: &Receiver<(Change, PathBuf)>, change: Change, path: &Path) {
        let end = Instant::now() + Duration::from_secs(5);
        while let Some(left) = end.checked_duration_since(Instant::now()) {
            if rx.recv_timeout(left).is_ok_and(|got| got == (change, path.to_path_buf())) {
                return;
            }
        }
        panic!("no {change:?} for {}", path.display());
    }

    #[test]
    fn a_new_file_is_added_then_modified() {
        let dir = Scratch::new("create");
        let (_watcher, rx) = watch(&dir.0);
        let file = dir.0.join("a.png");
        std::fs::write(&file, b"x").unwrap();
        expect(&rx, Change::Added, &file);
        expect(&rx, Change::Modified, &file);
        std::fs::write(&file, b"longer").unwrap();
        expect(&rx, Change::Modified, &file);
    }

    #[test]
    fn a_rename_reports_the_old_and_the_new_name() {
        let dir = Scratch::new("rename");
        let (old, new) = (dir.0.join("a.png"), dir.0.join("b.png"));
        std::fs::write(&old, b"x").unwrap();
        let (_watcher, rx) = watch(&dir.0);
        std::fs::rename(&old, &new).unwrap();
        expect(&rx, Change::RenamedFrom, &old);
        expect(&rx, Change::RenamedTo, &new);
    }

    #[test]
    fn a_file_moved_in_or_out_is_added_or_removed() {
        let dir = Scratch::new("move");
        let elsewhere = Scratch::new("move-elsewhere");
        let outside = elsewhere.0.join("a.png");
        std::fs::write(&outside, b"x").unwrap();
        let (_watcher, rx) = watch(&dir.0);
        let inside = dir.0.join("b.png");
        std::fs::rename(&outside, &inside).unwrap();
        expect(&rx, Change::Added, &inside);
        std::fs::rename(&inside, &outside).unwrap();
        expect(&rx, Change::Removed, &inside);
    }

    #[test]
    fn a_deleted_file_is_removed() {
        let dir = Scratch::new("remove");
        let file = dir.0.join("a.png");
        std::fs::write(&file, b"x").unwrap();
        let (_watcher, rx) = watch(&dir.0);
        std::fs::remove_file(&file).unwrap();
        expect(&rx, Change::Removed, &file);
    }

    #[test]
    fn subfolders_are_not_watched() {
        let dir = Scratch::new("shallow");
        let inner = dir.0.join("inner");
        std::fs::create_dir(&inner).unwrap();
        let (_watcher, rx) = watch(&dir.0);
        std::fs::write(inner.join("a.png"), b"x").unwrap();
        let top = dir.0.join("b.png");
        std::fs::write(&top, b"x").unwrap();
        // The folder's own entry may report a change, never what is inside.
        let end = Instant::now() + Duration::from_secs(1);
        while let Some(left) = end.checked_duration_since(Instant::now()) {
            if let Ok((_, path)) = rx.recv_timeout(left) {
                assert!(path == inner || path == top, "{}", path.display());
            }
        }
    }

    #[test]
    fn a_burst_of_files_is_all_reported() {
        let dir = Scratch::new("burst");
        let (_watcher, rx) = watch(&dir.0);
        let files: HashSet<PathBuf> = (0..300).map(|i| dir.0.join(format!("shot {i}.png"))).collect();
        for file in &files {
            std::fs::write(file, b"x").unwrap();
        }
        let mut added = HashSet::new();
        let end = Instant::now() + Duration::from_secs(10);
        while added != files {
            let left = end.checked_duration_since(Instant::now()).expect("some files were not reported");
            if let Ok((Change::Added, path)) = rx.recv_timeout(left) {
                added.insert(path);
            }
        }
    }

    #[test]
    fn dropping_stops_the_thread_at_once() {
        let dir = Scratch::new("drop");
        let (watcher, rx) = watch(&dir.0);
        // Let the thread settle into its wait.
        std::thread::sleep(Duration::from_millis(100));
        let started = Instant::now();
        drop(watcher);
        assert!(started.elapsed() < Duration::from_millis(500), "{:?}", started.elapsed());
        // Nothing is reported any more, and the sender went with the thread.
        std::fs::write(dir.0.join("a.png"), b"x").unwrap();
        assert!(rx.recv_timeout(Duration::from_millis(200)).is_err_and(|e| e == mpsc::RecvTimeoutError::Disconnected));
    }

    #[test]
    fn a_missing_folder_cannot_be_watched() {
        let dir = Scratch::new("missing");
        assert!(DirWatcher::start(&dir.0.join("nope"), |_, _| {}).is_err());
    }

    #[test]
    fn a_deleted_folder_ends_the_watch() {
        let dir = Scratch::new("deleted");
        let (_watcher, rx) = watch(&dir.0);
        std::fs::remove_dir_all(&dir.0).unwrap();
        // The thread ends and drops the sender: nothing is left to wait for.
        let end = Instant::now() + Duration::from_secs(5);
        loop {
            match rx.recv_timeout(end.saturating_duration_since(Instant::now())) {
                Err(mpsc::RecvTimeoutError::Disconnected) => break,
                Err(mpsc::RecvTimeoutError::Timeout) => panic!("the watch did not end"),
                Ok(_) => {}
            }
        }
    }

    #[test]
    fn records_are_split_by_their_offsets() {
        fn record(next: u32, action: u32, name: &str) -> Vec<u8> {
            let units: Vec<u8> = name.encode_utf16().flat_map(u16::to_le_bytes).collect();
            [next.to_le_bytes(), action.to_le_bytes(), (units.len() as u32).to_le_bytes()]
                .into_iter()
                .flatten()
                .chain(units)
                .collect()
        }
        let first = record(0, 1, "a.png");
        let mut buf = record(first.len() as u32, 1, "a.png");
        buf.extend(record(0, 5, "é b.png"));
        let got: Vec<_> = entries(&buf).collect();
        assert_eq!(got, [(1, "a.png".into()), (5, "é b.png".into())]);
        // A record cut short ends the list instead of reading past it.
        assert_eq!(entries(&buf[..buf.len() - 3]).count(), 1);
        assert_eq!(entries(&[]).count(), 0);
    }
}
