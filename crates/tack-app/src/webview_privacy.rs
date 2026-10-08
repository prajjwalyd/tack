//! Keeps the board's web view from calling Microsoft's online services.
//!
//! Tack's own page makes no network requests, but the WebView2 runtime under
//! it is a cut-down Edge, and on its own it opens connections at startup
//! (docs/privacy.md has the measurements). Most of that is stopped by the
//! browser arguments in tauri.conf.json:
//!
//! - `msOneAuthWAM` in `--disable-features`: Edge's sign-in component, which
//!   otherwise asks Windows for a token for the PC's Microsoft account and
//!   fetches that account's profile from `substrate.office.com`.
//! - `DnsOverHttpsUpgrade` in `--disable-features`: Chromium's automatic
//!   secure DNS, which otherwise upgrades the PC's DNS servers to their
//!   DNS-over-HTTPS equivalent (Google's 8.8.8.8 to `dns.google`) and probes
//!   it, even though the page never resolves a name.
//! - `--no-proxy-server`: no proxy auto-detection, so no `wpad` lookup on
//!   the local network.
//! - `msSmartScreenProtection`, `--disable-background-networking` and
//!   `--disable-component-update`: SmartScreen, Edge's configuration and
//!   experiment downloads, and component updates.
//!
//! The rest are web view settings, applied here once the window exists.
//! None of them is visible to the user: the board has no text fields, and
//! only ever shows Tack's own files.

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
    let Some(window) = app.get_webview_window(WINDOW_LABEL) else { return };
    let result = window.with_webview(|platform| {
        if let Err(e) = unsafe { configure(&platform.controller()) } {
            eprintln!("tack: cannot apply the web view's privacy settings: {e}");
        }
    });
    if let Err(e) = result {
        eprintln!("tack: cannot apply the web view's privacy settings: {e}");
    }
}

/// The same settings for another of Tack's windows (the Shortcuts dialog).
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
    // SmartScreen: no reputation checks of the pages and downloads (there are
    // none but Tack's own) against Microsoft's servers. This is the API
    // Microsoft documents for it; the `msSmartScreenProtection` argument does
    // the same on older runtimes.
    if let Ok(settings8) = settings.cast::<ICoreWebView2Settings8>() {
        settings8.SetIsReputationCheckingRequired(false)?;
    }
    // Autofill and password saving: the board has no forms, so both are
    // simply off, along with any lookups Edge's autofill might make online.
    if let Ok(settings4) = settings.cast::<ICoreWebView2Settings4>() {
        settings4.SetIsGeneralAutofillEnabled(false)?;
        settings4.SetIsPasswordAutosaveEnabled(false)?;
    }
    Ok(())
}
