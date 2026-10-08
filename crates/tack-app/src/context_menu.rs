//! The native right-click menu on a print: Copy, Open, Edit, Show in
//! Explorer, (Save to Pictures, for a capture), Keep or Stop keeping, Unpin,
//! Move to Recycle Bin. On a note: Copy (Copy link), Open link (for a link),
//! Open or Edit the text file, and the rest. Menu item ids are
//! `ctx:<action>:<print id>`. It opens at the pointer, or under the focused
//! print when the keyboard asked for it (Shift+F10, the Menu key).

use tack_core::Origin;
use tack_windows::shell;
use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::{AppHandle, Manager, PhysicalPosition};

use crate::captures;
use crate::ipc::events::Removal;
use crate::prints;
use crate::reveal::WINDOW_LABEL;
use crate::state::lock;

/// The prefix of this menu's item ids.
pub const ID_PREFIX: &str = "ctx:";

/// Shows the menu at the pointer, or at `at` (physical px relative to the
/// window), and waits until it closes.
pub fn show(app: &AppHandle, id: &str, at: Option<(f64, f64)>) -> Result<(), String> {
    let window = app.get_webview_window(WINDOW_LABEL).ok_or("no window")?;
    let item = |action: &str, label: &str| {
        MenuItem::with_id(app, format!("{ID_PREFIX}{action}:{id}"), label, true, None::<&str>)
            .map_err(|e| e.to_string())
    };
    let separator = PredefinedMenuItem::separator(app).map_err(|e| e.to_string())?;
    let (capture, kept, note, link) = {
        let s = lock(app);
        let print = s.board.find(id);
        let note = print.is_some_and(|p| p.is_note());
        (
            print.is_some_and(|p| p.origin == Origin::Capture) && !note,
            print.is_some_and(|p| p.kept),
            note,
            print.and_then(|p| p.note.as_ref()).is_some_and(|n| n.link.is_some()),
        )
    };
    let keep = if kept { item("unkeep", "Stop keeping")? } else { item("keep", "Keep")? };
    let (copy, open) = match (note, link) {
        (true, true) => ("Copy link", "Open link"),
        (true, false) => ("Copy text", "Open"),
        _ => ("Copy", "Open"),
    };
    let menu = Menu::with_items(
        app,
        &[
            &item("copy", copy)?,
            &item("open", open)?,
            &item("edit", "Edit")?,
            &item("reveal", "Show in Explorer")?,
            &separator,
            &keep,
            &item("discard", "Unpin")?,
            &item("recycle", "Move to Recycle Bin")?,
        ],
    )
    .map_err(|e| e.to_string())?;
    // A capture lives in Tack's own folder until it is saved somewhere real.
    if capture {
        menu.insert(&item("save", "Save to Pictures")?, 4).map_err(|e| e.to_string())?;
    }
    // A link's text file is of little use; it opens in the browser instead.
    if link {
        menu.remove_at(2).map_err(|e| e.to_string())?;
    }
    // Never tuck under an open menu.
    lock(app).view.menu_open = true;
    let result = match at {
        Some((x, y)) => window.popup_menu_at(&menu, PhysicalPosition::new(x, y)),
        None => window.popup_menu(&menu),
    };
    lock(app).view.menu_open = false;
    result.map_err(|e| e.to_string())
}

/// A click on one of this menu's items; `rest` is the id after the prefix.
pub fn handle(app: &AppHandle, rest: &str) {
    let Some((action, id)) = rest.split_once(':') else { return };
    match action {
        "copy" => {
            let (app, id) = (app.clone(), id.to_string());
            std::thread::spawn(move || {
                if let Err(e) = prints::copy(&app, &id) {
                    eprintln!("tack: copy failed: {e}");
                }
            });
        }
        "open" => prints::open(app, id),
        "edit" => prints::edit(app, id),
        "reveal" => {
            if let Some(path) = prints::path_of(app, id) {
                captures::when_saved(&path, shell::show_in_explorer);
            }
        }
        "keep" => prints::set_kept(app, id, true),
        "unkeep" => prints::set_kept(app, id, false),
        "discard" => {
            prints::remove(app, id, Removal::Fall);
        }
        "save" => captures::save_to_pictures(app, id),
        "recycle" => prints::recycle(app, id),
        _ => {}
    }
}
