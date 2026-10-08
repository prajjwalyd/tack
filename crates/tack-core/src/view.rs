//! The board on screen: whether it is revealed, why, where its window is and
//! where the pointer may go before it tucks away. Shared by the pointer
//! poller, the overlay window and the IPC commands; plain data, so the rules
//! that drive it live with the code that has the pointer and the window.

use serde::Serialize;

/// The board area at the top of the window: board, frame and pinned prints,
/// CSS px. The window itself covers the monitor's work area (transparent and
/// click-through outside the board), see `tack-app`'s `reveal::Placement`.
pub const STRIP_H: f64 = 176.0;

/// Why the board came down. Sent to the UI with `board:reveal`.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum RevealReason {
    /// The pointer rested against the top edge.
    Edge,
    /// The show-or-hide shortcut (Win+Alt+S): a keyboard open, so the board
    /// takes keyboard focus.
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

    /// The smallest rectangle holding both.
    pub fn union(&self, other: &Rect) -> Rect {
        let (x, y) = (self.x.min(other.x), self.y.min(other.y));
        let right = (self.x + self.w).max(other.x + other.w);
        let bottom = (self.y + self.h).max(other.y + other.h);
        Rect { x, y, w: right - x, h: bottom - y }
    }

    /// The part inside `other`, if any.
    pub fn intersect(&self, other: &Rect) -> Option<Rect> {
        let (x, y) = (self.x.max(other.x), self.y.max(other.y));
        let right = (self.x + self.w).min(other.x + other.w);
        let bottom = (self.y + self.h).min(other.y + other.h);
        (right > x && bottom > y).then_some(Rect { x, y, w: right - x, h: bottom - y })
    }

    pub fn center(&self) -> (i32, i32) {
        (self.x + self.w / 2, self.y + self.h / 2)
    }

    /// The same rectangle in CSS px, relative to a window whose top-left is
    /// at `origin` (physical px) on a monitor scaled by `scale`.
    pub fn to_css(&self, origin: (i32, i32), scale: f64) -> CssRect {
        let s = if scale > 0.0 { scale } else { 1.0 };
        CssRect {
            x: (self.x - origin.0) as f64 / s,
            y: (self.y - origin.1) as f64 / s,
            w: self.w as f64 / s,
            h: self.h as f64 / s,
        }
    }
}

/// A rectangle in CSS px, as the UI gets it.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct CssRect {
    pub x: f64,
    pub y: f64,
    pub w: f64,
    pub h: f64,
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
    /// The one-time tip hanging under the board while it shows, relative to
    /// the window, as reported by the UI. Clickable like the board.
    pub tip: Option<Rect>,
    /// Id of the print being dragged out, if any.
    pub dragging: Option<String>,
    /// Revealed for something being dragged in from another app (carried
    /// to the top edge with the button held): the board stays while the
    /// button is held over the top strip, so it can be dropped on.
    pub drag_in: bool,
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
            tip: None,
            dragging: None,
            drag_in: false,
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
        self.rect.map(|r| self.on_screen(r))
    }

    /// The tip under the board, if one shows, in screen coordinates.
    pub fn tip_on_screen(&self) -> Option<Rect> {
        self.tip.map(|r| self.on_screen(r))
    }

    /// Whether the point (screen coordinates) is on the board or its tip:
    /// where the window takes clicks.
    pub fn hit(&self, x: i32, y: i32) -> bool {
        [self.board_on_screen(), self.tip_on_screen()].iter().flatten().any(|r| r.contains(x, y))
    }

    /// The board and its tip together, in screen coordinates.
    pub fn zone_on_screen(&self) -> Option<Rect> {
        match (self.board_on_screen(), self.tip_on_screen()) {
            (Some(b), Some(t)) => Some(b.union(&t)),
            (b, t) => b.or(t),
        }
    }

    fn on_screen(&self, r: Rect) -> Rect {
        Rect { x: self.window_pos.0 + r.x, y: self.window_pos.1 + r.y, w: r.w, h: r.h }
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

    #[test]
    fn the_tip_takes_clicks_like_the_board() {
        let mut view =
            View { window_pos: (100, 50), rect: Some(Rect { x: 10, y: 0, w: 20, h: 30 }), ..View::default() };
        assert!(!view.hit(115, 90));
        view.tip = Some(Rect { x: 12, y: 36, w: 10, h: 8 });
        assert!(view.hit(115, 90));
        assert!(!view.hit(111, 90), "between the tip's edge and the board's, the window lets clicks through");
        assert_eq!(view.zone_on_screen(), Some(Rect { x: 110, y: 50, w: 20, h: 44 }));
    }

    #[test]
    fn rects_unite_intersect_and_convert_to_css() {
        let a = Rect { x: 0, y: 0, w: 10, h: 10 };
        let b = Rect { x: 5, y: -5, w: 10, h: 10 };
        assert_eq!(a.union(&b), Rect { x: 0, y: -5, w: 15, h: 15 });
        assert_eq!(a.intersect(&b), Some(Rect { x: 5, y: 0, w: 5, h: 5 }));
        assert_eq!(a.intersect(&Rect { x: 10, y: 0, w: 5, h: 5 }), None);
        // A snip at (1000, 600) physical on a 200% monitor whose window
        // starts at (0, 0), and on a 150% monitor to the right of it.
        let snip = Rect { x: 1000, y: 600, w: 400, h: 300 };
        assert_eq!(snip.to_css((0, 0), 2.0), CssRect { x: 500.0, y: 300.0, w: 200.0, h: 150.0 });
        let snip = Rect { x: 3000, y: 150, w: 300, h: 150 };
        assert_eq!(snip.to_css((2880, 0), 1.5), CssRect { x: 80.0, y: 100.0, w: 200.0, h: 100.0 });
        assert_eq!(snip.to_css((2880, 0), 1.0).x, 120.0);
    }
}
