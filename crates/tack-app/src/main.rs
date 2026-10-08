//! Tack: a corkboard for your recent screenshots that slides down from the
//! top edge of the screen. Each new screenshot is pinned to it, and a print
//! can be copied, opened, edited or dragged out as a file.
//!
//! The board lives in a transparent, never-focused window that covers the top
//! of one monitor. The UI (`ui/`) draws and animates it; this binary wires
//! the board model (`tack-core`) to the Windows integrations
//! (`tack-windows`) and to the UI through the IPC in docs/ipc.md.
//! docs/architecture.md walks through how the pieces fit.

// No console window in release builds.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

/// One timestamped line on stderr in debug builds (see `trace.rs`); release
/// builds compile it away, arguments and all.
macro_rules! trace {
    ($($arg:tt)*) => {{
        #[cfg(debug_assertions)]
        {
            $crate::trace::line(format_args!($($arg)*));
        }
    }};
}

mod captures;
mod context_menu;
mod drag;
mod ipc;
mod prints;
mod reveal;
mod screenshot_files;
mod state;
#[cfg(debug_assertions)]
mod stress;
#[cfg(debug_assertions)]
mod trace;
mod tray;
mod webview_power;
mod webview_privacy;

use std::sync::Mutex;

use tack_core::capture;
use tack_windows::{autostart, edge_reveal, hotkey, screenshots, single_instance};
use tauri::RunEvent;

use crate::ipc::commands;
use crate::state::AppState;

fn main() {
    // One board is plenty: a second copy would pin every screenshot twice.
    if single_instance::already_running() {
        return;
    }

    #[cfg(debug_assertions)]
    trace::start();

    // Every command the UI may invoke; debug builds add the stress test's.
    macro_rules! handler {
        ($($extra:path),*) => {
            tauri::generate_handler![
                commands::board_ready,
                commands::set_board_rect,
                commands::copy_print,
                commands::open_print,
                commands::edit_print,
                commands::start_drag,
                commands::discard_print,
                commands::context_menu,
                commands::set_hovering,
                commands::set_kept,
                $($extra),*
            ]
        };
    }
    let builder = tauri::Builder::default().manage(Mutex::new(AppState::load()));
    #[cfg(debug_assertions)]
    let builder = builder.invoke_handler(handler!(stress::debug_ack));
    #[cfg(not(debug_assertions))]
    let builder = builder.invoke_handler(handler!());

    let app = builder
        .on_menu_event(|app, event| {
            let id = event.id().as_ref();
            if let Some(rest) = id.strip_prefix(context_menu::ID_PREFIX) {
                context_menu::handle(app, rest);
            } else {
                tray::handle(app, id);
            }
        })
        .setup(|app| {
            let handle = app.handle().clone();
            reveal::init_window(&handle);
            tray::build(&handle)?;
            // Keep the Run entry pointing at this exe if it was moved.
            if autostart::enabled() {
                autostart::set(true);
            }
            screenshot_files::restore(handle.clone());
            prints::age_out_hourly(handle.clone());
            screenshot_files::watch(handle.clone(), screenshots::folder());
            screenshot_files::watch(handle.clone(), capture::captures_folder());
            captures::start(handle.clone());
            edge_reveal::start(reveal::EdgeGlue(handle.clone()));
            let toggle = handle;
            hotkey::start(move || reveal::toggle(&toggle, tack_core::RevealReason::Hotkey));
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building Tack");

    app.run(|_app, event| {
        // The board window never closes, but if it ever did the app would
        // quit with it; only Quit in the tray ends Tack.
        if let RunEvent::ExitRequested { api, code: None, .. } = event {
            api.prevent_exit();
        }
    });
}
