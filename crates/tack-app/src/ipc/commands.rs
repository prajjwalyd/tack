//! What the UI can ask for.
//!
//! Plain commands run on the main thread, so they must be quick. Anything
//! slow (decoding a full image, a file to save or recycle) or blocking (a
//! popup menu, a wait for another thread) is async and does its work with
//! [`blocking`], on the blocking pool: the few async workers stay free and
//! the board keeps animating meanwhile.

use serde::{Deserialize, Serialize};
use tack_core::{Print, Rect};
use tauri::AppHandle;

use crate::ipc::events::Removal;
use crate::phone::{self, PhoneState};
use crate::settings_apply::{self, SettingsView};
use crate::shortcuts::{self, DialogState, Pair};
use crate::state::lock;
use crate::updates::{self, UpdateStatus};
use crate::{context_menu, drag, keyboard, notes, prints, reveal, settings_window};

/// Runs `job` on the async runtime's blocking pool and waits for it there,
/// so a slow body never ties up one of the few async workers.
async fn blocking<T: Send + 'static>(job: impl FnOnce() -> T + Send + 'static) -> T {
    match tauri::async_runtime::spawn_blocking(job).await {
        Ok(value) => value,
        // The job panicked: fail this command the way it would have inline.
        Err(e) => panic!("tack: a command's work stopped: {e}"),
    }
}

/// What `board_ready` returns.
#[derive(Serialize)]
pub struct Ready {
    prints: Vec<Print>,
    sound: bool,
    /// All the settings, so the board starts with its colour and motion.
    settings: SettingsView,
}

/// The UI has loaded and listens for events. Returns the prints already
/// pinned, in row order (restored ones may still be arriving as events).
#[tauri::command]
pub fn board_ready(app: AppHandle) -> Ready {
    let (prints, sound) = {
        let mut s = lock(&app);
        // From here on every change reaches the UI as an event.
        s.ui_ready = true;
        (s.board.prints().to_vec(), s.settings.sound)
    };
    Ready { prints, sound, settings: settings_apply::view(&app) }
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
    blocking(move || prints::copy(&app, &id)).await
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

/// Sync, so a quick unpin and keep apply in the order they were clicked;
/// both are cheap (saving is queued, recycling runs on its own thread).
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
    blocking(move || context_menu::show(&app, &id, at.map(|p| (p.x, p.y)))).await
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
    blocking(move || {
        let _line = notes::pin_text(&app, &text);
        trace!("dropped text: {_line}");
    })
    .await
}

/// A PNG or JPEG dropped on the board, as base64: pinned like a capture.
#[tauri::command]
pub async fn pin_image(app: AppHandle, name: String, data: String) {
    blocking(move || {
        let _line = notes::pin_dropped_image(&app, &name, &data);
        trace!("dropped picture: {_line}");
    })
    .await
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
    blocking(move || shortcuts::set(&app, &toggle, &pin)).await
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

/// The Settings window opens: every setting.
#[tauri::command]
pub fn settings_state(app: AppHandle) -> SettingsView {
    settings_apply::view(&app)
}

/// The settings below each save, update the tray and send `settings:changed`
/// to every window; each answers with the new settings. They are quick, so
/// plain commands.
#[tauri::command]
pub fn set_sound(app: AppHandle, on: bool) -> SettingsView {
    settings_apply::set_sound(&app, on)
}

#[tauri::command]
pub fn set_edge_reveal(app: AppHandle, on: bool) -> SettingsView {
    settings_apply::set_edge_reveal(&app, on)
}

#[tauri::command]
pub fn set_autostart(app: AppHandle, on: bool) -> SettingsView {
    settings_apply::set_autostart(&app, on)
}

/// "instant", "fast" or "normal"; anything else changes nothing.
#[tauri::command]
pub fn set_reveal(app: AppHandle, speed: String) -> SettingsView {
    settings_apply::set_reveal(&app, &speed)
}

/// A preset's id, "accent" or "#rrggbb"; anything else changes nothing.
#[tauri::command]
pub fn set_felt(app: AppHandle, value: String) -> SettingsView {
    settings_apply::set_felt(&app, &value)
}

/// A frame finish's id ("aluminium", "graphite", "brass", "silver"); anything
/// else changes nothing.
#[tauri::command]
pub fn set_frame(app: AppHandle, value: String) -> SettingsView {
    settings_apply::set_frame(&app, &value)
}

/// A note paper's id ("white", "cream", "butter", "blush", "sky", "mint");
/// anything else changes nothing.
#[tauri::command]
pub fn set_paper(app: AppHandle, value: String) -> SettingsView {
    settings_apply::set_paper(&app, &value)
}

/// The board colour, frame and note paper back to their defaults.
#[tauri::command]
pub fn reset_appearance(app: AppHandle) -> SettingsView {
    settings_apply::reset_appearance(&app)
}

#[tauri::command]
pub fn set_updates_check(app: AppHandle, on: bool) -> SettingsView {
    settings_apply::set_updates_check(&app, on)
}

/// Where updates stand. The window also hears `updates:status` on every change.
#[tauri::command]
pub fn update_status(app: AppHandle) -> UpdateStatus {
    updates::status(&app)
}

/// "Check now": asks the update server and answers with the result. Async:
/// it waits for the network.
#[tauri::command]
pub async fn check_for_updates(app: AppHandle) -> UpdateStatus {
    blocking(move || updates::check(&app)).await
}

/// "Install": downloads and verifies the update found, then starts its
/// installer, which restarts Tack. Answers with the status it ends in (and
/// sends it as `updates:status`): after a failure, the error; after a success
/// there is no answer, because Tack has quit.
#[tauri::command]
pub async fn install_update(app: AppHandle) -> UpdateStatus {
    blocking(move || updates::install(&app)).await
}

/// "Shortcuts…" in the Settings window. Async: building a window waits for
/// the event loop, which runs plain commands.
#[tauri::command]
pub async fn open_shortcuts(app: AppHandle) {
    blocking(move || shortcuts::open_dialog(&app)).await
}

/// "Use on your phone…" in the Settings window.
#[tauri::command]
pub async fn open_phone_link(app: AppHandle) {
    blocking(move || phone::window::open(&app)).await
}

#[tauri::command]
pub async fn close_settings(app: AppHandle) {
    settings_window::close(&app);
}

/// The phone window opens. Async: it may ask NetBird how it is.
#[tauri::command]
pub async fn phone_state(app: AppHandle) -> PhoneState {
    blocking(move || phone::state(&app)).await
}

/// The phone window's switch.
#[tauri::command]
pub async fn set_phone(app: AppHandle, on: bool) -> PhoneState {
    blocking(move || phone::set_on(&app, on)).await
}

/// Allow or Don't allow, for a device asking to use the board.
#[tauri::command]
pub async fn answer_device(app: AppHandle, key: String, allow: bool) -> PhoneState {
    blocking(move || phone::answer(&app, &key, allow)).await
}

/// Remove, for an allowed device.
#[tauri::command]
pub async fn forget_device(app: AppHandle, key: String) -> PhoneState {
    blocking(move || phone::forget(&app, &key)).await
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
