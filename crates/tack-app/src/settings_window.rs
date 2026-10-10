//! The Settings window (`ui/settings.html`): reveal speed, board colour, the
//! switches from the tray, and updates. Created while open, like the other
//! dialogs; every change goes through [`crate::settings_apply`].

use tauri::{AppHandle, Manager};

use crate::dialogs;

pub const LABEL: &str = "settings";
const PAGE: &str = "settings.html";
const TITLE: &str = "Tack settings";
/// The page scrolls inside it.
const SIZE: (f64, f64) = (520.0, 640.0);

/// Opens the window, or brings it to the front (the tray's "Settings…").
/// Not from the event loop's own thread: building a window waits for it.
pub fn open(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
        return;
    }
    if let Err(e) = dialogs::open(app, LABEL, PAGE, TITLE, SIZE) {
        eprintln!("tack: cannot open the Settings window: {e}");
    }
}

pub fn close(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.close();
    }
}
