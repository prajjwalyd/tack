//! The board for the keyboard. Opened with the show-or-hide shortcut, the
//! board takes keyboard focus (the page asks with `take_focus` once it has
//! come down) so the arrow keys, Enter, Delete and Esc work on it; when it
//! goes, the window that had the keyboard gets it back.
//!
//! Opened any other way (the edge, a new snip, the tray), the board never
//! takes focus: the page does not ask, and the window stays
//! `WS_EX_NOACTIVATE` as always.

use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};

use tack_core::RevealReason;
use tack_windows::focus;
use tauri::{AppHandle, Manager};
use webview2_com::Microsoft::Web::WebView2::Win32::COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC;
use windows::Win32::Foundation::HWND;

use crate::reveal::{self, board_hwnd, WINDOW_LABEL};
use crate::state::lock;

/// The window that had the foreground before the board took it (a raw
/// handle; 0 when the board does not have the keyboard).
static PREVIOUS: AtomicIsize = AtomicIsize::new(0);
/// The board has the keyboard and has not given it back yet.
static TAKEN: AtomicBool = AtomicBool::new(false);

/// The board has the keyboard and has not given it back yet.
pub fn taken() -> bool {
    TAKEN.load(Ordering::SeqCst)
}

/// Gives the board keyboard focus, if it is down because of the shortcut.
/// On the main thread.
pub fn take(app: &AppHandle) -> bool {
    {
        let s = lock(app);
        if !s.view.shown || s.view.reason != RevealReason::Hotkey {
            return false;
        }
    }
    let Some(hwnd) = board_hwnd(app) else { return false };
    let Some(previous) = focus::take(hwnd) else {
        eprintln!("tack: the board could not take the keyboard focus");
        return false;
    };
    if previous != hwnd {
        PREVIOUS.store(previous.0 as isize, Ordering::SeqCst);
    }
    if let Some(window) = app.get_webview_window(WINDOW_LABEL) {
        let _ = window.with_webview(|platform| unsafe {
            let _ = platform.controller().MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
        });
    }
    trace!("keyboard: the board took focus (foreground process {:?})", focus::foreground_process());
    TAKEN.store(true, Ordering::SeqCst);
    true
}

/// Gives the keyboard back to the window that had it, if the board still
/// has it. Whatever ends the reveal (Esc, the shortcut, a click elsewhere)
/// ends in `reveal::tuck`, which calls this even if the page never asks.
/// On the main thread.
pub fn release(app: &AppHandle) {
    if !TAKEN.swap(false, Ordering::SeqCst) {
        return;
    }
    let previous = PREVIOUS.swap(0, Ordering::SeqCst);
    if let Some(hwnd) = board_hwnd(app) {
        let previous = (previous != 0).then_some(HWND(previous as *mut _));
        focus::give_back(hwnd, previous);
        trace!("keyboard: focus given back (foreground process {:?})", focus::foreground_process());
    }
}

/// Esc: the board goes back up.
pub fn hide(app: &AppHandle) {
    trace!("tuck requested: Esc on the board");
    reveal::tuck(app);
}
