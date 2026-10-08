//! What the UI can ask for. See docs/ipc.md.
//!
//! Plain commands run on the main thread, async ones on a worker. Anything
//! slow (decoding a full image) or blocking (a popup menu) is async, so the
//! board keeps animating meanwhile.

use serde::{Deserialize, Serialize};
use tack_core::{Print, Rect};
use tauri::AppHandle;

use crate::ipc::events::Removal;
use crate::phone::{self, PhoneState};
use crate::shortcuts::{self, DialogState, Pair};
use crate::state::lock;
use crate::{context_menu, drag, keyboard, notes, prints, reveal};

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

/// A screenshot in its default app; a link note in the browser.
#[tauri::command]
pub fn open_print(app: AppHandle, id: String) {
    prints::open(&app, &id);
}

/// Paint for a screenshot, Notepad for a note.
#[tauri::command]
pub fn edit_print(app: AppHandle, id: String) {
    prints::edit(&app, &id);
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

/// A point from the UI, physical px relative to the window.
#[derive(Deserialize)]
pub struct UiPoint {
    x: f64,
    y: f64,
}

/// Async: the menu is modal and this waits until it closes. At the pointer,
/// or `at` the focused print when the keyboard asked for it.
#[tauri::command]
pub async fn context_menu(app: AppHandle, id: String, at: Option<UiPoint>) -> Result<(), String> {
    context_menu::show(&app, &id, at.map(|p| (p.x, p.y)))
}

/// A rectangle from the UI, physical px relative to the window.
#[derive(Deserialize)]
pub struct UiRect {
    x: f64,
    y: f64,
    w: f64,
    h: f64,
}

/// The one-time tip under the board is showing there, or (`null`) closed.
#[tauri::command]
pub fn set_tip(app: AppHandle, rect: Option<UiRect>) {
    let rect = rect.map(|r| Rect {
        x: r.x.round() as i32,
        y: r.y.round() as i32,
        w: r.w.round().max(0.0) as i32,
        h: r.h.round().max(0.0) as i32,
    });
    reveal::set_tip(&app, rect);
}

/// Only a hint; the pointer poller is the authority on hovering.
#[tauri::command]
pub fn set_hovering(hovering: bool) {
    let _ = hovering;
}

/// Text dropped on the board: pinned as a note (a link if it is one). Async:
/// it saves a file and may wait to reveal.
#[tauri::command]
pub async fn pin_text(app: AppHandle, text: String) {
    let _line = notes::pin_text(&app, &text);
    trace!("dropped text: {_line}");
}

/// A PNG or JPEG dropped on the board, as base64: pinned like a capture.
#[tauri::command]
pub async fn pin_image(app: AppHandle, name: String, data: String) {
    let _line = notes::pin_dropped_image(&app, &name, &data);
    trace!("dropped picture: {_line}");
}

/// The board came down for the keyboard (the shortcut) and wants focus.
#[tauri::command]
pub fn take_focus(app: AppHandle) -> bool {
    keyboard::take(&app)
}

/// The board is going: the keyboard goes back where it was.
#[tauri::command]
pub fn release_focus(app: AppHandle) {
    keyboard::release(&app);
}

/// Esc on the board.
#[tauri::command]
pub fn hide_board(app: AppHandle) {
    keyboard::hide(&app);
}

/// The Shortcuts dialog opens: the shortcuts, how they registered, and the
/// defaults.
#[tauri::command]
pub fn shortcuts_state(app: AppHandle) -> DialogState {
    shortcuts::dialog_state(&app)
}

/// The Shortcuts dialog's Save: saved only if both work. Async: it waits
/// for the hotkey thread.
#[tauri::command]
pub async fn set_shortcuts(app: AppHandle, toggle: String, pin: String) -> Pair {
    shortcuts::set(&app, &toggle, &pin)
}

/// The Shortcuts dialog listens for a chord (shortcuts paused) or is done.
#[tauri::command]
pub async fn pause_shortcuts(paused: bool) {
    shortcuts::pause(paused);
}

#[tauri::command]
pub async fn close_shortcuts(app: AppHandle) {
    shortcuts::close_dialog(&app);
}

/// The phone window opens. Async: it may ask NetBird how it is.
#[tauri::command]
pub async fn phone_state(app: AppHandle) -> PhoneState {
    phone::state(&app)
}

/// The phone window's switch.
#[tauri::command]
pub async fn set_phone(app: AppHandle, on: bool) -> PhoneState {
    phone::set_on(&app, on)
}

/// Allow or Don't allow, for a device asking to use the board.
#[tauri::command]
pub async fn answer_device(app: AppHandle, key: String, allow: bool) -> PhoneState {
    phone::answer(&app, &key, allow)
}

/// Remove, for an allowed device.
#[tauri::command]
pub async fn forget_device(app: AppHandle, key: String) -> PhoneState {
    phone::forget(&app, &key)
}

/// "Get NetBird": NetBird's install page, in the browser.
#[tauri::command]
pub fn open_netbird_download() {
    tack_windows::shell::open_url(phone::NETBIRD_INSTALL);
}

#[tauri::command]
pub async fn close_phone_link(app: AppHandle) {
    phone::window::close(&app);
}
