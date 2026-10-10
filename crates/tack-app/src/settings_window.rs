//! The Settings window (`ui/settings.html`): the phone card, the switches from
//! the tray, reveal speed, board colour, the shortcuts and updates. Created
//! while open, like the other dialogs; every change goes through
//! [`crate::settings_apply`].

use tauri::{AppHandle, Manager, WindowEvent};

use crate::ipc::events;
use crate::{dialogs, shortcuts};

pub const LABEL: &str = "settings";
const PAGE: &str = "settings.html";
const TITLE: &str = "Tack settings";
/// The page scrolls inside it.
const SIZE: (f64, f64) = (520.0, 640.0);

/// The sections the page can be opened at: the ids of its groups
/// (`ui/scripts/settings/main.js`).
pub const SECTIONS: [&str; 4] = ["phone", "appearance", "shortcuts", "updates"];

/// The page to load, scrolled to `section` (the page reads its `#hash`). A
/// section the page does not have is left off.
fn page_for(section: Option<&str>) -> String {
    match section.filter(|s| SECTIONS.contains(s)) {
        Some(section) => format!("{PAGE}#{section}"),
        None => PAGE.to_string(),
    }
}

/// Opens the window, or brings it to the front (the tray's "Settings…"), at
/// `section` if there is one (a window that is open is told with
/// `settings:section`). Not from the event loop's own thread: building a
/// window waits for it.
pub fn open(app: &AppHandle, section: Option<&str>) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
        if let Some(section) = section.filter(|s| SECTIONS.contains(s)) {
            events::settings_section(app, section);
        }
        return;
    }
    match dialogs::open(app, LABEL, &page_for(section), TITLE, SIZE) {
        Ok(window) => {
            window.on_window_event(|event| {
                if let WindowEvent::Destroyed = event {
                    // Closed while a shortcut was listening: the hotkeys come back.
                    shortcuts::pause(false);
                }
            });
        }
        Err(e) => eprintln!("tack: cannot open the Settings window: {e}"),
    }
}

pub fn close(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(LABEL) {
        let _ = window.close();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_page_opens_at_a_known_section() {
        assert_eq!(page_for(None), "settings.html");
        assert_eq!(page_for(Some("shortcuts")), "settings.html#shortcuts");
        assert_eq!(page_for(Some("phone")), "settings.html#phone");
        assert_eq!(page_for(Some("../x")), "settings.html");
    }
}
