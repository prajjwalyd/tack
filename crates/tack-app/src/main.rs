//! Tack: a corkboard for your recent screenshots that slides down from the
//! top edge of the screen. This binary wires the board model (`tack-core`)
//! to the Windows integrations (`tack-windows`) and to the UI (`ui/`,
//! through the IPC in docs/ipc.md). docs/architecture.md has the overview.

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
mod dialogs;
mod drag;
mod ipc;
mod keyboard;
mod notes;
mod phone;
mod prints;
mod reveal;
mod screenshot_files;
mod shortcuts;
mod state;
#[cfg(debug_assertions)]
mod stress;
#[cfg(debug_assertions)]
mod trace;
mod tray;
mod webview_power;
mod webview_privacy;

use std::sync::Mutex;

use tack_core::{capture, note};
use tack_windows::{autostart, edge_reveal, sandbox, screenshots, single_instance};
use tauri::RunEvent;

use crate::ipc::commands;
use crate::state::AppState;

fn main() {
    // Started inside another app's MSIX container: run as a normal app
    // instead (see tack-windows/src/sandbox.rs).
    if let Some(host) = sandbox::host_package() {
        if sandbox::relaunch_outside(&host) {
            return;
        }
    }
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
                commands::set_tip,
                commands::pin_text,
                commands::pin_image,
                commands::take_focus,
                commands::release_focus,
                commands::hide_board,
                commands::shortcuts_state,
                commands::set_shortcuts,
                commands::pause_shortcuts,
                commands::close_shortcuts,
                commands::phone_state,
                commands::set_phone,
                commands::answer_device,
                commands::forget_device,
                commands::open_netbird_download,
                commands::close_phone_link,
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
            // Registered before the tray is built, so its menu can say if
            // another app has one of them.
            shortcuts::start(&handle);
            tray::build(&handle)?;
            // Keep the Run entry pointing at this exe if it was moved.
            if autostart::enabled() {
                autostart::set(true);
            }
            screenshot_files::restore(handle.clone());
            prints::age_out_hourly(handle.clone());
            screenshot_files::watch(handle.clone(), screenshots::folder());
            screenshot_files::watch(handle.clone(), capture::captures_folder());
            screenshot_files::watch(handle.clone(), note::notes_folder());
            captures::start(handle.clone());
            phone::start(handle.clone());
            edge_reveal::start(reveal::EdgeGlue(handle.clone()));
            #[cfg(debug_assertions)]
            notes::debug::start(&handle);
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
