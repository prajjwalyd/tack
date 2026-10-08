//! The Screenshots folder: where it is, and a watcher that reports what
//! happens in it (also used for Tack's own captures folder). Tack has no
//! capture tool of its own; whatever saves into this folder ends up on the
//! board. Deciding what an event means for the board is the caller's job.

use std::path::PathBuf;

use notify::event::{ModifyKind, RenameMode};
use notify::{Event, EventKind, RecursiveMode, Watcher};
use windows::Win32::System::Com::CoTaskMemFree;
use windows::Win32::UI::Shell::{FOLDERID_Screenshots, SHGetKnownFolderPath, KF_FLAG_CREATE};

/// The Screenshots known folder (where Win + Shift + S and Print Screen
/// save), created if it does not exist yet.
pub fn folder() -> PathBuf {
    let known = unsafe {
        SHGetKnownFolderPath(&FOLDERID_Screenshots, KF_FLAG_CREATE, None).ok().map(|p| {
            let path = PathBuf::from(p.to_string().unwrap_or_default());
            CoTaskMemFree(Some(p.0 as *const _));
            path
        })
    };
    let folder = known.filter(|p| !p.as_os_str().is_empty()).unwrap_or_else(|| {
        let home = std::env::var_os("USERPROFILE").map(PathBuf::from).unwrap_or_default();
        home.join("Pictures").join("Screenshots")
    });
    let _ = std::fs::create_dir_all(&folder);
    folder
}

/// Something happened to a file in a watched folder.
#[derive(Debug)]
pub enum FolderEvent {
    /// Created, or renamed or moved into the folder.
    Arrived(PathBuf),
    /// Deleted, or renamed or moved away. Editors often save by deleting and
    /// renaming a temp file into place, so the file may be back a moment later.
    Gone(PathBuf),
    /// Written to.
    Changed(PathBuf),
}

/// Watches `folder` (not its subfolders) for as long as the app runs, calling
/// `on_event` on the watcher's thread.
pub fn watch(folder: PathBuf, on_event: impl Fn(FolderEvent) + Send + 'static) {
    let _ = std::fs::create_dir_all(&folder);
    let watcher = notify::recommended_watcher(move |res: notify::Result<Event>| match res {
        Ok(event) => translate(event, &on_event),
        Err(e) => eprintln!("tack: watcher error: {e}"),
    });
    match watcher {
        Ok(mut watcher) => {
            if let Err(e) = watcher.watch(&folder, RecursiveMode::NonRecursive) {
                eprintln!("tack: cannot watch {}: {e}", folder.display());
                return;
            }
            // Lives as long as the app.
            Box::leak(Box::new(watcher));
        }
        Err(e) => eprintln!("tack: cannot create watcher: {e}"),
    }
}

fn translate(event: Event, on_event: &impl Fn(FolderEvent)) {
    match event.kind {
        EventKind::Create(_) | EventKind::Modify(ModifyKind::Name(RenameMode::To)) => {
            for path in event.paths {
                on_event(FolderEvent::Arrived(path));
            }
        }
        EventKind::Modify(ModifyKind::Name(RenameMode::Both)) => {
            if let [from, to] = event.paths.as_slice() {
                on_event(FolderEvent::Gone(from.clone()));
                on_event(FolderEvent::Arrived(to.clone()));
            }
        }
        EventKind::Remove(_) | EventKind::Modify(ModifyKind::Name(RenameMode::From)) => {
            for path in event.paths {
                on_event(FolderEvent::Gone(path));
            }
        }
        EventKind::Modify(_) => {
            for path in event.paths {
                on_event(FolderEvent::Changed(path));
            }
        }
        _ => {}
    }
}
