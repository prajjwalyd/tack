//! The board for the keyboard. Opened with the show-or-hide shortcut, the
//! board takes keyboard focus (the page asks with `take_focus` once it has
//! come down) so the arrow keys, Enter, Delete and Esc work on it; when it
//! goes, the window that had the keyboard gets it back.
//!
//! Opened any other way (the edge, a new snip, the tray), the board never
//! takes focus: the page does not ask, and the window stays
//! `WS_EX_NOACTIVATE` as always.

use std::sync::atomic::{AtomicIsize, AtomicU64, Ordering};
use std::time::Duration;

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
/// Counts the times the board took the keyboard, so a watch left over from
/// an earlier time never gives back a later one.
static SESSION: AtomicU64 = AtomicU64::new(0);
/// How often the watch below looks whether the board went.
const WATCH: Duration = Duration::from_millis(100);

/// Gives the board keyboard focus, if it is down because of the shortcut.
/// On the main thread.
pub fn take(app: &AppHandle) -> bool {
    let generation = {
        let s = lock(app);
        if !s.view.shown || s.view.reason != RevealReason::Hotkey {
            return false;
        }
        s.view.generation
    };
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
    let session = SESSION.fetch_add(1, Ordering::SeqCst) + 1;
    watch(app, generation, session);
    true
}

/// Gives the keyboard back to the window that had it, if the board still
/// has it. On the main thread.
pub fn release(app: &AppHandle) {
    let previous = PREVIOUS.swap(0, Ordering::SeqCst);
    if let Some(hwnd) = board_hwnd(app) {
        let previous = (previous != 0).then_some(HWND(previous as *mut _));
        focus::give_back(hwnd, previous);
        trace!("keyboard: focus given back (foreground process {:?})", focus::foreground_process());
    }
}

/// Esc: the board goes back up.
pub fn hide(app: &AppHandle) {
    reveal::tuck(app);
}

/// Whatever ends the reveal (Esc, the shortcut, a click elsewhere), the
/// keyboard goes back and the window is unfocusable again, even if the page
/// never asks.
fn watch(app: &AppHandle, generation: u64, session: u64) {
    let app = app.clone();
    let _ = std::thread::Builder::new().name("tack-keyboard".into()).spawn(move || loop {
        std::thread::sleep(WATCH);
        if SESSION.load(Ordering::SeqCst) != session {
            return;
        }
        if lock(&app).view.generation != generation {
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || {
                if SESSION.load(Ordering::SeqCst) == session {
                    release(&handle);
                }
            });
            return;
        }
    });
}
