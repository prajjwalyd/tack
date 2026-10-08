//! Every event the backend sends the UI, in one place: the names (mirrored in
//! `ui/scripts/ipc.js` and documented in docs/ipc.md) and one function per
//! event that builds its payload. Nothing else calls `emit`.
//!
//! Emitting is fire and forget: if the webview is gone there is nobody to
//! tell, so errors are ignored.

use serde::Serialize;
use serde_json::json;
use tack_core::{Print, RevealReason};
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
// `board:gust` is part of the contract too, but the backend never sends it
// today: the UI schedules its own gusts (the preview harness sends it).
pub const SETTINGS: &str = "board:settings";
pub const POINTER_LEFT: &str = "board:pointer-left";

/// How the UI animates an unpinned print.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Removal {
    /// The pin pops out and the print tumbles into the overhang.
    Fall,
    /// A short fade, for prints whose file was deleted or moved elsewhere.
    Quiet,
}

/// A print was pinned. `animate`: a live arrival, pressed on with a pin
/// sound; otherwise it just appears (restored at startup).
pub fn print_added(app: &AppHandle, print: &Print, animate: bool) {
    let _ = app.emit(PRINT_ADDED, json!({ "print": print, "animate": animate }));
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

pub fn settings(app: &AppHandle, sound: bool) {
    let _ = app.emit(SETTINGS, json!({ "sound": sound }));
}

pub fn pointer_left(app: &AppHandle) {
    let _ = app.emit(POINTER_LEFT, ());
}
