//! The Settings window (`ui/settings.html`): the phone card, the switches from
//! the tray, reveal speed, board colour, the shortcuts and updates. Created
//! while open, like the other dialogs; every change goes through
//! [`crate::settings_apply`].

use tack_core::view::STRIP_H;
use tack_core::Rect;
use tauri::{AppHandle, Manager, PhysicalPosition, PhysicalSize, WebviewWindow, WindowEvent};

use crate::ipc::events;
use crate::{dialogs, reveal, shortcuts};

pub const LABEL: &str = "settings";
const PAGE: &str = "settings.html";
const TITLE: &str = "Tack settings";
/// The page scrolls inside it.
const SIZE: (f64, f64) = (520.0, 640.0);

/// Settings opens below the board, which it holds down, so it must not cover
/// it. The board's strip ([`STRIP_H`]: board, frame and pinned prints) ends
/// this far above the window, and the window keeps this far from the bottom
/// of the work area, CSS px.
const GAP: f64 = 16.0;
/// The shortest the window's page area is made to fit under the board, CSS
/// px; the page scrolls inside it.
const MIN_HEIGHT: f64 = 420.0;

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
        place(app, &window);
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
        if let Some(section) = section.filter(|s| SECTIONS.contains(s)) {
            events::settings_section(app, section);
        }
        reveal::hold_for_settings(app, true);
        return;
    }
    match dialogs::open_hidden(app, LABEL, &page_for(section), TITLE, SIZE) {
        Ok(window) => {
            place(app, &window);
            let _ = window.show();
            let _ = window.set_focus();
            let handle = app.clone();
            window.on_window_event(move |event| {
                if let WindowEvent::Destroyed = event {
                    // Closed while a shortcut was listening: the hotkeys come back.
                    shortcuts::pause(false);
                    // However it closed (X, Esc, the page's own close): the
                    // board it held down goes back up.
                    reveal::hold_for_settings(&handle, false);
                }
            });
            // While it is open the board stays down, so changes show live.
            reveal::hold_for_settings(app, true);
        }
        Err(e) => eprintln!("tack: cannot open the Settings window: {e}"),
    }
}

/// Where the window goes: on the monitor the board is down on, centred
/// across its work area and under the board.
///
/// `chrome` is the window's frame (title bar and borders), physical px: the
/// window's size on screen less its page area. Pure, so it is tested: `work`
/// is the monitor's work area (the taskbar left out) and `scale` its DPI / 96,
/// both physical px; the answer is the window's outer rectangle, physical px.
///
/// 520 CSS px wide (less on a narrow monitor), 640 tall but cut to what is
/// left below the board, and not under [`MIN_HEIGHT`] unless the whole work
/// area is shorter. If that is not left below the board, the window moves up
/// over the board's lower part rather than off the screen.
fn placement(work: Rect, scale: f64, chrome: (i32, i32)) -> Rect {
    let px = |css: f64| (css * scale).round() as i32;
    let w = (px(SIZE.0) + chrome.0).min(work.w);
    let x = work.x + (work.w - w) / 2;
    let top = work.y + px(STRIP_H + GAP);
    let limit = work.y + work.h - px(GAP);
    let h = (px(SIZE.1) + chrome.1).min(limit - top).max(px(MIN_HEIGHT) + chrome.1).min(work.h);
    let y = if top + h > limit { (limit - h).max(work.y) } else { top };
    Rect { x, y, w, h }
}

/// Moves and sizes the window per [`placement`] for the monitor the board is
/// on. Called before it is shown, and again whenever it is brought forward.
fn place(app: &AppHandle, window: &WebviewWindow) {
    let (Ok(outer), Ok(inner), Ok(own_scale)) = (window.outer_size(), window.inner_size(), window.scale_factor())
    else {
        return;
    };
    let on = reveal::board_placement(app);
    // The frame is the same size in CSS px; on another monitor it is drawn
    // at that monitor's scale.
    let frame = |a: u32, b: u32| ((a.saturating_sub(b)) as f64 / own_scale * on.scale).round() as i32;
    let chrome = (frame(outer.width, inner.width), frame(outer.height, inner.height));
    let work = Rect { x: on.pos.0, y: on.pos.1, w: on.size.0, h: on.size.1 };
    let at = placement(work, on.scale, chrome);
    let size = PhysicalSize::new((at.w - chrome.0).max(1) as u32, (at.h - chrome.1).max(1) as u32);
    // Move, size, move: crossing to a monitor with another DPI makes the
    // window resize itself on the way, which the size then corrects.
    let position = PhysicalPosition::new(at.x, at.y);
    let _ = window.set_position(position);
    let _ = window.set_size(size);
    let _ = window.set_position(position);
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

    /// A 1080p monitor's work area at a scale, the taskbar 40 CSS px high.
    fn work_1080(scale: f64) -> Rect {
        Rect { x: 0, y: 0, w: 1920, h: 1080 - (40.0 * scale).round() as i32 }
    }

    /// Frame of a normal window: 16 px of borders and a 39 px title bar at 100%.
    fn frame(scale: f64) -> (i32, i32) {
        ((16.0 * scale).round() as i32, (39.0 * scale).round() as i32)
    }

    #[test]
    fn at_100_percent_it_sits_under_the_board_at_full_height() {
        let at = placement(work_1080(1.0), 1.0, frame(1.0));
        assert_eq!((at.w, at.h), (520 + 16, 640 + 39));
        assert_eq!(at.y, 176 + 16, "the board's strip, then a gap");
        assert_eq!(at.x, (1920 - 536) / 2, "centred");
    }

    #[test]
    fn at_150_percent_on_1080p_it_is_cut_to_fit_and_clear_of_the_board() {
        let (scale, chrome) = (1.5, frame(1.5));
        let work = work_1080(scale);
        let at = placement(work, scale, chrome);
        let board_bottom = (STRIP_H * scale).round() as i32;
        assert!(at.y >= board_bottom + (GAP * scale).round() as i32, "{at:?}");
        assert_eq!(at.y + at.h, work.y + work.h - (GAP * scale).round() as i32, "a gap above the taskbar");
        let page_css = (at.h - chrome.1) as f64 / scale;
        assert!((MIN_HEIGHT..640.0).contains(&page_css), "{page_css}");
        assert_eq!(at.w, (520.0 * scale).round() as i32 + chrome.0);
    }

    #[test]
    fn a_second_monitor_places_it_in_its_own_work_area() {
        // A monitor to the right at 200%, 3840x2160, taskbar 80 px at the bottom.
        let work = Rect { x: 1920, y: 0, w: 3840, h: 2160 - 80 };
        let at = placement(work, 2.0, frame(2.0));
        assert_eq!(at.w, 1040 + 32);
        assert_eq!(at.x, 1920 + (3840 - at.w) / 2);
        assert_eq!(at.y, 384);
        assert_eq!(at.h, 1280 + 78, "room for all of it");
        // One above a primary monitor, its work area starting below zero.
        let at = placement(Rect { x: 0, y: -1080, w: 1920, h: 1040 }, 1.0, frame(1.0));
        assert_eq!(at.y, -1080 + 176 + 16);
    }

    #[test]
    fn a_short_screen_overlaps_the_board_rather_than_leaving_the_screen() {
        // 560 high: only 352 px under the board; the page keeps 420.
        let work = Rect { x: 0, y: 0, w: 1024, h: 560 };
        let at = placement(work, 1.0, frame(1.0));
        assert_eq!(at.h, 420 + 39);
        assert_eq!(at.y + at.h, 560 - 16);
        assert!(at.y < 176 + 16, "over the board's lower part");
        // Shorter than that: the whole work area, from its top.
        let work = Rect { x: 0, y: 30, w: 1024, h: 400 };
        let at = placement(work, 1.0, frame(1.0));
        assert_eq!((at.y, at.h), (30, 400));
    }

    #[test]
    fn a_narrow_monitor_limits_the_width() {
        let at = placement(Rect { x: 100, y: 0, w: 480, h: 900 }, 1.0, frame(1.0));
        assert_eq!((at.x, at.w), (100, 480));
    }
}
