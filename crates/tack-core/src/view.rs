//! The board on screen: whether it is revealed, why, where its window is and
//! where the pointer may go before it tucks away. Shared by the pointer
//! poller, the overlay window and the IPC commands; plain data, so the rules
//! that drive it live with the code that has the pointer and the window.

use serde::Serialize;

/// The board area of the window: board, frame and pinned prints, CSS px.
pub const STRIP_H: f64 = 176.0;
/// Transparent space below the board for drop animations and shadows, CSS px.
/// The window is click-through there, so it never blocks anything.
pub const OVERHANG: f64 = 220.0;

/// Why the board came down. Sent to the UI with `board:reveal`.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum RevealReason {
    /// The pointer rested against the top edge.
    Edge,
    /// Ctrl+Alt+T.
    Hotkey,
    /// A new screenshot arrived; the board peeks out briefly to show it.
    New,
    /// The tray icon or its menu.
    Tray,
}

/// A rectangle in physical pixels.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Rect {
    pub x: i32,
    pub y: i32,
    pub w: i32,
    pub h: i32,
}

impl Rect {
    pub fn contains(&self, x: i32, y: i32) -> bool {
        x >= self.x && x < self.x + self.w && y >= self.y && y < self.y + self.h
    }

    pub fn inflate(&self, by: i32) -> Rect {
        Rect { x: self.x - by, y: self.y - by, w: self.w + 2 * by, h: self.h + 2 * by }
    }
}

/// The window and the pointer, as seen by the poller and the reveal logic.
#[derive(Debug)]
pub struct View {
    /// Revealed (or about to be). Cleared the moment a tuck starts, even
    /// though the window stays visible for the 300 ms slide.
    pub shown: bool,
    /// Bumped on every reveal and tuck, so a late hide never hides a board
    /// that was revealed again in the meantime.
    pub generation: u64,
    pub reason: RevealReason,
    /// Revealed by the hotkey or the tray: the leave timer is ignored until
    /// the pointer has been over the board once. A click outside or the
    /// hotkey still tucks it.
    pub stay_open: bool,
    /// End of a new-screenshot peek; after it the board tucks unless the
    /// pointer has come over it.
    pub peek_until: Option<std::time::Instant>,
    /// The pointer has been over the board since it was revealed.
    pub entered: bool,
    /// After a tuck the pointer has to leave the top edge before the edge can
    /// bring the board back, or it would bounce straight back down.
    pub edge_armed: bool,
    /// Window top-left on screen and its size, physical px.
    pub window_pos: (i32, i32),
    pub window_size: (i32, i32),
    /// Top of the monitor (not the work area): the leave zone reaches up to it.
    pub monitor_top: i32,
    /// Monitor DPI / 96.
    pub scale: f64,
    /// The board, relative to the window, as reported by the UI.
    pub rect: Option<Rect>,
    /// Id of the print being dragged out, if any.
    pub dragging: Option<String>,
    pub menu_open: bool,
    /// Whether the window currently lets clicks through.
    pub ignoring: bool,
    /// A full screen app is in front.
    pub fullscreen: bool,
    /// What is in front (and counted in `fullscreen`) is Snipping Tool's own
    /// overlay, up while Win + Shift + S picks a region. It keeps the edge
    /// from revealing, but never holds back or tucks a new capture's board.
    pub snipping: bool,
}

impl Default for View {
    fn default() -> Self {
        View {
            shown: false,
            generation: 0,
            reason: RevealReason::Edge,
            stay_open: false,
            peek_until: None,
            entered: false,
            edge_armed: true,
            window_pos: (0, 0),
            window_size: (0, 0),
            monitor_top: 0,
            scale: 1.0,
            rect: None,
            dragging: None,
            menu_open: false,
            ignoring: true,
            fullscreen: false,
            snipping: false,
        }
    }
}

impl View {
    /// The board rect in screen coordinates.
    pub fn board_on_screen(&self) -> Option<Rect> {
        self.rect.map(|r| Rect { x: self.window_pos.0 + r.x, y: self.window_pos.1 + r.y, w: r.w, h: r.h })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_board_rect_follows_the_window() {
        let view = View { window_pos: (100, 50), rect: Some(Rect { x: 10, y: 0, w: 20, h: 30 }), ..View::default() };
        let r = view.board_on_screen().unwrap();
        assert_eq!(r, Rect { x: 110, y: 50, w: 20, h: 30 });
        assert!(r.contains(110, 50));
        assert!(!r.contains(130, 50));
        assert_eq!(r.inflate(2), Rect { x: 108, y: 48, w: 24, h: 34 });
    }
}
