//! Dragging a print out of the board: marks the drag in the shared view (so
//! the board never tucks under it), runs the shell's drag loop on the UI
//! thread, and afterwards unpins the print if the drop moved its file.

use std::path::PathBuf;
use std::time::Duration;

use tack_core::thumbnail;
use tack_windows::drag_out;
use tauri::AppHandle;

use crate::captures;
use crate::ipc::events::{self, Removal};
use crate::prints;
use crate::reveal::board_hwnd;
use crate::state::lock;

/// Delays (ms) at which to test whether the dropped file still exists;
/// Explorer may finish a move asynchronously.
const RECHECKS: [u64; 3] = [0, 600, 1500];

/// Starts the drag on the main thread and returns at once. The UI hears
/// `board:print-dragging` now and `board:print-drag-ended` when the drop is
/// over.
pub fn start(app: &AppHandle, id: String) {
    // A capture pinned moments ago may still be being written.
    if let Some(path) = prints::path_of(app, &id) {
        captures::wait_saved(&path);
    }
    let (path, thumb) = {
        let mut s = lock(app);
        let Some(print) = s.board.find(&id) else { return };
        let path = print.path.clone();
        let thumb = print.thumb.clone();
        s.view.dragging = Some(id.clone());
        (path, thumb)
    };
    let image = thumbnail::drag_image(&thumb);
    events::print_dragging(app, &id);

    let handle = app.clone();
    let scheduled = app.run_on_main_thread(move || {
        let hwnd = board_hwnd(&handle);
        // Modal: returns once the button is released somewhere.
        if let Err(e) = drag_out::drag_file(hwnd, &path, image) {
            eprintln!("tack: drag failed: {e}");
        }
        finish(&handle, id, path);
    });
    if scheduled.is_err() {
        lock(app).view.dragging = None;
    }
}

fn finish(app: &AppHandle, id: String, path: PathBuf) {
    lock(app).view.dragging = None;
    events::print_drag_ended(app, &id);
    let app = app.clone();
    std::thread::spawn(move || {
        let mut waited = 0;
        for at in RECHECKS {
            std::thread::sleep(Duration::from_millis(at - waited));
            waited = at;
            if !path.exists() {
                prints::remove_path(&app, &path, Removal::Quiet);
                return;
            }
        }
    });
}
