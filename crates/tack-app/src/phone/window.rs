//! The "Tack on your phone" window (`ui/phone-link.html`): the switch, the
//! QR code and address, "Let pixel use your board?", and the allowed
//! devices.

use tauri::{AppHandle, Manager, UserAttentionType};

use crate::dialogs;

pub const LABEL: &str = "phone-link";
const PAGE: &str = "phone-link.html";
const TITLE: &str = "Tack on your phone";
const SIZE: (f64, f64) = (420.0, 640.0);

/// Opens the window, or brings it to the front (the tray's "Use on your
/// phone…"). Not from the event loop's own thread.
pub fn open(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
        return;
    }
    if let Err(e) = dialogs::open(app, LABEL, PAGE, TITLE, SIZE) {
        eprintln!("tack: cannot open the phone window: {e}");
    }
}

/// A device asks to use the board: the window shows the question without
/// taking the keyboard, and flashes in the taskbar, so a key the user was
/// about to press can never answer it.
pub fn ask(app: &AppHandle) {
    let window = match app.get_webview_window(LABEL) {
        Some(window) => {
            let _ = window.unminimize();
            let _ = window.show();
            Some(window)
        }
        None => dialogs::build(app, LABEL, PAGE, TITLE, SIZE, false)
            .map_err(|e| eprintln!("tack: cannot open the phone window: {e}"))
            .ok(),
    };
    if let Some(window) = window {
        let _ = window.request_user_attention(Some(UserAttentionType::Informational));
    }
}

pub fn close(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.close();
    }
}
