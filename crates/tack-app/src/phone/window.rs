//! The "Tack on your phone" window (`ui/phone-link.html`): the switch, the
//! QR code and address, "Let pixel use your board?", and the allowed
//! devices. Opened from the tray, and by itself, focused, when a new device
//! asks.

use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder};

use crate::reveal;

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
    let board = app.config().app.windows.iter().find(|w| w.label == reveal::WINDOW_LABEL).cloned();
    let mut builder = WebviewWindowBuilder::new(app, LABEL, WebviewUrl::App("phone-link.html".into()))
        .title("Tack on your phone")
        .inner_size(420.0, 640.0)
        .resizable(false)
        .maximizable(false)
        .minimizable(false)
        .center()
        .focused(true)
        .visible(true);
    // The board's web view runtime is shared, as for the Shortcuts dialog.
    if let Ok(local) = app.path().local_data_dir() {
        builder = builder.data_directory(local.join("Tack").join("WebView2"));
    }
    if let Some(args) = board.and_then(|w| w.additional_browser_args) {
        builder = builder.additional_browser_args(&args);
    }
    match builder.build() {
        Ok(window) => crate::webview_privacy::apply_to(&window),
        Err(e) => eprintln!("tack: cannot open the phone window: {e}"),
    }
}

pub fn close(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.close();
    }
}
