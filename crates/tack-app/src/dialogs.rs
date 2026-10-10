//! Tack's small dialog windows (Settings and the phone link). They share the
//! board's WebView2 runtime: the same data folder, and the same browser
//! arguments (a second set would not be allowed to share it, and they keep
//! the dialogs off the network too).

use tauri::{AppHandle, WebviewUrl, WebviewWindow, WebviewWindowBuilder};

use crate::reveal;

/// Builds and shows a fixed-size, focused, centred dialog showing `page`
/// (under ui/), with the board's privacy settings applied. Not from the
/// event loop's own thread: building a window waits for it.
pub fn open(app: &AppHandle, label: &str, page: &str, title: &str, size: (f64, f64)) -> tauri::Result<WebviewWindow> {
    build(app, label, page, title, size, true)
}

/// As [`open`], but the dialog only takes the keyboard if `focus`: one that
/// opens on its own (a device asking) must not catch what the user types.
pub fn build(
    app: &AppHandle,
    label: &str,
    page: &str,
    title: &str,
    size: (f64, f64),
    focus: bool,
) -> tauri::Result<WebviewWindow> {
    build_with(app, label, page, title, size, focus, true)
}

/// As [`open`], but the window is created hidden, centred, so the caller can
/// place it and then `show` and focus it (no jump from the centre).
pub fn open_hidden(
    app: &AppHandle,
    label: &str,
    page: &str,
    title: &str,
    size: (f64, f64),
) -> tauri::Result<WebviewWindow> {
    build_with(app, label, page, title, size, true, false)
}

fn build_with(
    app: &AppHandle,
    label: &str,
    page: &str,
    title: &str,
    size: (f64, f64),
    focus: bool,
    visible: bool,
) -> tauri::Result<WebviewWindow> {
    let board = app.config().app.windows.iter().find(|w| w.label == reveal::WINDOW_LABEL).cloned();
    let mut builder = WebviewWindowBuilder::new(app, label, WebviewUrl::App(page.into()))
        .title(title)
        .inner_size(size.0, size.1)
        .resizable(false)
        .maximizable(false)
        .minimizable(false)
        .center()
        .focused(focus)
        .visible(visible);
    if let Some(dir) = reveal::webview_data_dir(app) {
        builder = builder.data_directory(dir);
    }
    if let Some(args) = board.and_then(|w| w.additional_browser_args) {
        builder = builder.additional_browser_args(&args);
    }
    let window = builder.build()?;
    crate::webview_privacy::apply_to(&window);
    Ok(window)
}
