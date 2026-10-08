//! Every event the backend sends the UI, in one place: the names (mirrored in
//! `ui/scripts/ipc.js`) and one function per
//! event that builds its payload. Nothing else calls `emit`.
//!
//! Emitting is fire and forget: if the webview is gone there is nobody to
//! tell, so errors are ignored.

use serde::Serialize;
use serde_json::json;
use tack_core::{CssRect, Print, RevealReason};
use tauri::{AppHandle, Emitter};

pub const PRINT_ADDED: &str = "board:print-added";
pub const PRINT_REMOVED: &str = "board:print-removed";
pub const PRINT_UPDATED: &str = "board:print-updated";
pub const ORDER_CHANGED: &str = "board:order-changed";
pub const PRINT_COPIED: &str = "board:print-copied";
pub const PRINT_DRAGGING: &str = "board:print-dragging";
pub const PRINT_DRAG_ENDED: &str = "board:print-drag-ended";
pub const REVEAL: &str = "board:reveal";
pub const TUCK: &str = "board:tuck";
pub const WARM_UP: &str = "board:warm-up";
// `board:gust` is in the contract but only the preview harness sends it; the
// UI schedules its own gusts.
pub const SETTINGS: &str = "board:settings";
pub const POINTER_LEFT: &str = "board:pointer-left";
pub const NOTICE: &str = "board:notice";
pub const PHONE_STATE: &str = "phone:state";

/// How the UI animates an unpinned print.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Removal {
    /// The pin pops out and the print tumbles into the overhang.
    Fall,
    /// A short fade, for prints whose file was deleted or moved elsewhere.
    Quiet,
}

/// A new capture's way onto the board: it flies from where it was taken.
#[derive(Clone, Debug, Serialize)]
pub struct Flight {
    /// Where it was taken, in CSS px relative to the board's window (the
    /// window the board comes down in for it). If `found` is false, a small
    /// rectangle of the picture's shape centred on the pointer instead.
    pub from: CssRect,
    /// The picture to fly with: a JPEG data URL, long side at most 1600 px,
    /// sharper than the print's thumbnail.
    pub image: String,
    /// The picture was found on screen, so `from` is exactly where it was.
    pub found: bool,
    /// Show the one-time tip about Snipping Tool's notification after it
    /// lands (see the `set_tip` command).
    pub tip: bool,
}

/// A print was pinned. `animate`: a live arrival, pressed on with a pin
/// sound; otherwise it just appears (restored at startup). `flight`: a new
/// capture that flies in from where it was taken.
pub fn print_added(app: &AppHandle, print: &Print, animate: bool, flight: Option<&Flight>) {
    let _ = app.emit(PRINT_ADDED, json!({ "print": print, "animate": animate, "flight": flight }));
}

pub fn print_removed(app: &AppHandle, id: &str, how: Removal) {
    let _ = app.emit(PRINT_REMOVED, json!({ "id": id, "how": how }));
}

/// The print's picture, size, name, file or kept state changed.
pub fn print_updated(app: &AppHandle, print: &Print) {
    let _ = app.emit(PRINT_UPDATED, json!({ "print": print }));
}

/// The row's order changed (a new print, or one kept or no longer kept):
/// every pinned print's id, in row order.
pub fn order_changed(app: &AppHandle, ids: &[String]) {
    let _ = app.emit(ORDER_CHANGED, json!({ "ids": ids }));
}

pub fn print_copied(app: &AppHandle, id: &str) {
    let _ = app.emit(PRINT_COPIED, json!({ "id": id }));
}

pub fn print_dragging(app: &AppHandle, id: &str) {
    let _ = app.emit(PRINT_DRAGGING, json!({ "id": id }));
}

/// The drop is over. The webview never saw the mouse button come up.
pub fn print_drag_ended(app: &AppHandle, id: &str) {
    let _ = app.emit(PRINT_DRAG_ENDED, json!({ "id": id }));
}

pub fn reveal(app: &AppHandle, reason: RevealReason) {
    let _ = app.emit(REVEAL, json!({ "reason": reason }));
}

pub fn tuck(app: &AppHandle) {
    let _ = app.emit(TUCK, ());
}

/// Once at startup, while the hidden board's window is shown for a moment:
/// draw the board once, unseen, so its first reveal draws at once.
pub fn warm_up(app: &AppHandle) {
    let _ = app.emit(WARM_UP, ());
}

pub fn settings(app: &AppHandle, sound: bool) {
    let _ = app.emit(SETTINGS, json!({ "sound": sound }));
}

pub fn pointer_left(app: &AppHandle) {
    let _ = app.emit(POINTER_LEFT, ());
}

/// A short message under the board: something could not be pinned
/// ("Nothing selected", "Already pinned"...). Also read out by screen readers.
pub fn notice(app: &AppHandle, text: &str) {
    let _ = app.emit(NOTICE, json!({ "text": text }));
}

/// The phone window's state changed (see `phone::PhoneState`).
pub fn phone_state(app: &AppHandle, state: &crate::phone::PhoneState) {
    let _ = app.emit_to(crate::phone::window::LABEL, PHONE_STATE, state);
}
