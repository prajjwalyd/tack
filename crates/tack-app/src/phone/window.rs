//! The "Tack on your phone" window (`ui/phone-link.html`): the switch, the
//! QR code and address, "Let pixel use your board?", and the allowed
//! devices. Opened from the tray, and by itself, focused, when a new device
//! asks.

use tauri::{AppHandle, Manager};

use crate::dialogs;

pub const LABEL: &str = "phone-link";

/// Opens the window, or brings it to the front. Not from the event loop's
/// own thread (a handler there would wait on itself).
pub fn open(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
        return;
    }
    if let Err(e) = dialogs::open(app, LABEL, "phone-link.html", "Tack on your phone", (420.0, 640.0)) {
        eprintln!("tack: cannot open the phone window: {e}");
    }
}

pub fn close(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.close();
    }
}
