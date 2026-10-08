//! What the UI can ask for. See docs/ipc.md.
//!
//! Plain commands run on the main thread, async ones on a worker. Anything
//! slow (decoding a full image) or blocking (a popup menu) is async, so the
//! board keeps animating meanwhile.

use serde::Serialize;
use tack_core::{Print, Rect};
use tack_windows::shell;
use tauri::AppHandle;

use crate::context_menu;
use crate::drag;
use crate::ipc::events::Removal;
use crate::prints;
use crate::state::lock;

/// What `board_ready` returns.
#[derive(Serialize)]
pub struct Ready {
    prints: Vec<Print>,
    sound: bool,
}

/// The UI has loaded and listens for events. Returns the prints already
/// pinned, in row order (restored ones may still be arriving as events).
#[tauri::command]
pub fn board_ready(app: AppHandle) -> Ready {
    let mut s = lock(&app);
    // From here on every change reaches the UI as an event.
    s.ui_ready = true;
    Ready { prints: s.board.prints().to_vec(), sound: s.settings.sound }
}

/// Where the board is, in physical px relative to the window, for
/// click-through and leave detection.
#[tauri::command]
pub fn set_board_rect(app: AppHandle, x: f64, y: f64, w: f64, h: f64) {
    lock(&app).view.rect = Some(Rect {
        x: x.round() as i32,
        y: y.round() as i32,
        w: w.round().max(0.0) as i32,
        h: h.round().max(0.0) as i32,
    });
}

#[tauri::command]
pub async fn copy_print(app: AppHandle, id: String) -> Result<(), String> {
    prints::copy(&app, &id)
}

#[tauri::command]
pub fn open_print(app: AppHandle, id: String) {
    if let Some(path) = prints::path_of(&app, &id) {
        shell::open(&path);
    }
}

#[tauri::command]
pub fn edit_print(app: AppHandle, id: String) {
    if let Some(path) = prints::path_of(&app, &id) {
        shell::edit(&path);
    }
}

/// Async so the OLE drag loop starts after this call has returned.
#[tauri::command]
pub async fn start_drag(app: AppHandle, id: String) {
    drag::start(&app, id);
}

#[tauri::command]
pub fn discard_print(app: AppHandle, id: String) {
    prints::remove(&app, &id, Removal::Fall);
}

/// Keeps a print at the front of the row, or lets it rejoin the history.
#[tauri::command]
pub fn set_kept(app: AppHandle, id: String, kept: bool) {
    prints::set_kept(&app, &id, kept);
}

/// Async: the menu is modal and this waits until it closes.
#[tauri::command]
pub async fn context_menu(app: AppHandle, id: String) -> Result<(), String> {
    context_menu::show(&app, &id)
}

/// Only a hint; the pointer poller is the authority on hovering.
#[tauri::command]
pub fn set_hovering(hovering: bool) {
    let _ = hovering;
}
