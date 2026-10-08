//! Keeps Tack's web views from calling Microsoft's online services. Tack's
//! own pages make no network requests, but the WebView2 runtime under them
//! is a cut-down Edge that opens connections on its own. Most of that is
//! stopped by the browser arguments in tauri.conf.json (docs/privacy.md
//! says what each stops); the rest are web view settings, applied here once
//! a window exists. None of them is visible to the user.

use tauri::{AppHandle, Manager};
use webview2_com::Microsoft::Web::WebView2::Win32::{
    ICoreWebView2Controller, ICoreWebView2Settings4, ICoreWebView2Settings8,
};
use windows::core::Interface;

use crate::reveal::WINDOW_LABEL;

/// Applies the settings to the board's web view. `with_webview` queues the
/// closure on the main thread, so this may be called from anywhere, once the
/// window exists.
pub fn apply(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(WINDOW_LABEL) {
        apply_to(&window);
    }
}

/// The same settings for another of Tack's windows.
pub fn apply_to(window: &tauri::WebviewWindow) {
    let result = window.with_webview(|platform| {
        if let Err(e) = unsafe { configure(&platform.controller()) } {
            eprintln!("tack: cannot apply the web view's privacy settings: {e}");
        }
    });
    if let Err(e) = result {
        eprintln!("tack: cannot apply the web view's privacy settings: {e}");
    }
}

/// Each setting needs a newer runtime than the last; on a runtime too old
/// for one, the cast fails and that setting is skipped.
unsafe fn configure(controller: &ICoreWebView2Controller) -> windows::core::Result<()> {
    let settings = controller.CoreWebView2()?.Settings()?;
    // SmartScreen: no reputation checks against Microsoft's servers. The
    // `msSmartScreenProtection` argument does the same on older runtimes.
    if let Ok(settings8) = settings.cast::<ICoreWebView2Settings8>() {
        settings8.SetIsReputationCheckingRequired(false)?;
    }
    // Autofill and password saving: no forms here, and no online lookups.
    if let Ok(settings4) = settings.cast::<ICoreWebView2Settings4>() {
        settings4.SetIsGeneralAutofillEnabled(false)?;
        settings4.SetIsPasswordAutosaveEnabled(false)?;
    }
    Ok(())
}
