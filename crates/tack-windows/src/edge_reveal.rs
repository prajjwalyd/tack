//! Polls the pointer to decide when the board should come down from the top
//! edge and when it should go back up, and flips the window's click-through
//! as the pointer crosses the board's border.
//!
//! The edge also opens for a drag: something (text, a link, a picture,
//! files) carried from another app to the top edge with the button held,
//! to be dropped on the board. [`DragGate`] tells that from a press at the
//! top (a title bar, a tab) and from a window being moved there to snap:
//! the press must start well below the edge and arrive quickly, the pointer
//! must show one of Windows' drag cursors (not the arrow, text or resize
//! cursor), and the window in front must not be in a move or size.
//!
//! A plain polling thread: global mouse hooks would cost every app on the
//! system a little latency. The poller reads and updates the shared
//! [`View`] through an [`EdgeHost`], which also carries out what it decides.

use std::collections::VecDeque;
use std::time::{Duration, Instant};

use tack_core::view::STRIP_H;
use tack_core::{Rect, RevealReason, Settings, View};
use windows::Win32::Foundation::POINT;

use crate::pointer::{self, VK_LBUTTON, VK_RBUTTON};
use crate::{overlay, snipping_tool};

const POLL_SHOWN: Duration = Duration::from_millis(25);
const POLL_HIDDEN: Duration = Duration::from_millis(40);
/// Dwell time at the top edge before revealing. Reaching for a title bar or a
/// browser tab passes through the edge too, and should not count.
const EDGE_DWELL: Duration = Duration::from_millis(400);
/// Movement that still counts as resting, px.
const EDGE_JITTER: i32 = 3;
/// The same two for a drag held at the edge: the user is waiting for the
/// board with something in hand, and a hand holding a button wobbles more.
const DRAG_DWELL: Duration = Duration::from_millis(220);
const DRAG_JITTER: i32 = 8;
/// How far below the monitor top still counts as "the very top", px.
const EDGE_BAND: i32 = 2;
/// Grace period once the pointer is outside the board's zone, so a brief
/// overshoot does not tuck it.
const LEAVE_DELAY: Duration = Duration::from_millis(350);
/// Slack around the board before the pointer counts as gone, CSS px.
const LEAVE_MARGIN: f64 = 24.0;
/// SHQueryUserNotificationState is not free; nobody notices half a second.
const FULLSCREEN_CHECK: Duration = Duration::from_millis(500);

/// What the poller needs from the app.
pub trait EdgeHost: Send + 'static {
    /// Runs `f` on the shared view, with the settings, under the board lock.
    fn with_view<R>(&self, f: impl FnOnce(&mut View, &Settings) -> R) -> R;
    /// The pointer rested on the top edge: reveal with [`RevealReason::Edge`].
    fn reveal(&self);
    fn tuck(&self);
    /// Make the window click-through (`true`) or not.
    fn set_click_through(&self, ignore: bool);
    /// The window just became click-through, so the UI may never see the
    /// pointer leave a print; tell it.
    fn pointer_left(&self);
}

enum Action {
    None,
    Reveal,
    Tuck,
}

/// What the poller remembers between ticks.
struct Poller {
    hot_since: Option<(Instant, POINT)>,
    away_since: Option<Instant>,
    prev_left: bool,
    prev_right: bool,
    /// The left button went down over the board and is still held: a press,
    /// a long press or the start of a drag. Never tuck or flip click-through
    /// under it.
    press_inside: bool,
    last_fullscreen_check: Option<Instant>,
    drag: DragGate,
}

/// Starts the poller on a thread of its own.
pub fn start<H: EdgeHost>(host: H) {
    std::thread::Builder::new()
        .name("tack-pointer".into())
        .spawn(move || {
            let mut p = Poller {
                hot_since: None,
                away_since: None,
                prev_left: false,
                prev_right: false,
                press_inside: false,
                last_fullscreen_check: None,
                drag: DragGate::default(),
            };
            loop {
                let shown = host.with_view(|v, _| v.shown);
                std::thread::sleep(if shown { POLL_SHOWN } else { POLL_HIDDEN });
                p.tick(&host);
            }
        })
        .expect("pointer thread");
}

impl Poller {
    fn tick(&mut self, host: &impl EdgeHost) {
        let now = Instant::now();
        let pt = pointer::cursor_pos();
        let left = pointer::key_down(VK_LBUTTON.0);
        let right = pointer::key_down(VK_RBUTTON.0);
        let left_pressed = left && !self.prev_left;
        let right_pressed = right && !self.prev_right;
        self.prev_left = left;
        self.prev_right = right;
        if !left {
            self.press_inside = false;
        }

        // (full screen, and it is Snipping Tool's own overlay)
        let fullscreen = if self.last_fullscreen_check.is_none_or(|t| now - t >= FULLSCREEN_CHECK) {
            self.last_fullscreen_check = Some(now);
            let fs = overlay::fullscreen_app_in_front();
            Some((fs, fs && snipping_tool::in_front()))
        } else {
            None
        };

        let mut click_through = None;
        let action = host.with_view(|v, settings| {
            if let Some((fs, snipping)) = fullscreen {
                v.fullscreen = fs;
                v.snipping = snipping;
            }

            if !v.shown {
                self.away_since = None;
                self.press_inside = false;
                v.drag_in = false;
                let mon = overlay::monitor_at(pt);
                let at_top = pt.y < mon.monitor.top + EDGE_BAND;
                if !at_top {
                    v.edge_armed = true;
                }
                // Something carried here from another app, held over the edge.
                let dragging = self.drag.sample(now, pt.y - mon.monitor.top, left && !right, at_top)
                    && pointer::cursor_kind().may_be_dragging()
                    && !pointer::window_moving();
                let buttons_ok = (!left && !right) || dragging;
                let (dwell, jitter) = if dragging { (DRAG_DWELL, DRAG_JITTER) } else { (EDGE_DWELL, EDGE_JITTER) };
                if !settings.edge_reveal || !at_top || !v.edge_armed || !buttons_ok || v.fullscreen {
                    self.hot_since = None;
                    Action::None
                } else {
                    match self.hot_since {
                        Some((since, anchor))
                            if (pt.x - anchor.x).abs() < jitter && (pt.y - anchor.y).abs() < jitter =>
                        {
                            if now - since >= dwell {
                                self.hot_since = None;
                                v.drag_in = dragging;
                                Action::Reveal
                            } else {
                                Action::None
                            }
                        }
                        _ => {
                            self.hot_since = Some((now, pt));
                            Action::None
                        }
                    }
                }
            } else {
                self.hot_since = None;
                self.drag.sample(now, 0, false, false);
                if !left {
                    // Dropped (or let go elsewhere): from now on it leaves
                    // like any reveal from the edge.
                    v.drag_in = false;
                }
                let scale = v.scale;
                // The board, and the one-time tip under it while that shows.
                let board = v.zone_on_screen();
                // The UI already adds a small margin around the board.
                let over_board = v.hit(pt.x, pt.y);

                // Above the board counts as inside: the zone stretches from the
                // board's margin to the monitor's top edge, because the pointer
                // tends to drift back up the way it came.
                let mut in_zone = board
                    .map(|r| {
                        let z = r.inflate((LEAVE_MARGIN * scale).round() as i32);
                        let top = z.y.min(v.monitor_top);
                        Rect { x: z.x, y: top, w: z.w, h: z.y + z.h - top }.contains(pt.x, pt.y)
                    })
                    .unwrap_or(false);
                if in_zone {
                    v.entered = true;
                    // Visited: from now on it leaves like any other reveal.
                    v.stay_open = false;
                } else if (!v.entered && v.reason == RevealReason::Edge) || v.drag_in {
                    // Fresh from the edge the pointer may be anywhere along
                    // the top; the whole strip counts until it finds the
                    // board, and for as long as something is carried to it.
                    let strip = Rect {
                        x: v.window_pos.0,
                        y: v.monitor_top,
                        w: v.window_size.0,
                        h: v.window_pos.1 - v.monitor_top + (STRIP_H * scale).round() as i32,
                    };
                    in_zone = strip.contains(pt.x, pt.y);
                }

                let held = v.dragging.is_some() || v.menu_open;
                if left_pressed && over_board {
                    self.press_inside = true;
                }
                // Not the taskbar: a click on the tray icon toggles the board
                // itself when the button comes up, and tucking here on the way
                // down would make it bounce straight back.
                let clicked_outside = (left_pressed || right_pressed) && !over_board && !pointer::on_taskbar(pt);

                if !held && !self.press_inside && over_board == v.ignoring {
                    v.ignoring = !over_board;
                    click_through = Some(v.ignoring);
                }

                let peeking = !v.entered && v.peek_until.is_some_and(|t| now < t);
                let busy = held || self.press_inside || v.stay_open || peeking;

                // A full-screen app coming to the front sends the edge's and a
                // new capture's board back up; Snipping Tool's overlay only
                // the edge's, since a new capture's board may well come down
                // while that overlay is still fading out.
                let fullscreen_tucks = v.fullscreen
                    && match v.reason {
                        RevealReason::Edge => true,
                        RevealReason::New => !v.snipping,
                        RevealReason::Hotkey | RevealReason::Tray => false,
                    };
                if (!held && clicked_outside) || (!held && fullscreen_tucks) {
                    if cfg!(debug_assertions) {
                        eprintln!(
                            "tack: tuck: {} (left {left_pressed}, right {right_pressed}, at {}, {})",
                            if clicked_outside { "a click outside" } else { "full screen" },
                            pt.x,
                            pt.y
                        );
                    }
                    Action::Tuck
                } else if in_zone || busy {
                    self.away_since = None;
                    Action::None
                } else {
                    let since = *self.away_since.get_or_insert(now);
                    if now - since >= LEAVE_DELAY {
                        Action::Tuck
                    } else {
                        Action::None
                    }
                }
            }
        });

        if let Some(ignore) = click_through {
            host.set_click_through(ignore);
            if ignore {
                // Once click-through, the webview may never see the pointer
                // leave, which would leave a print stuck in its hover state.
                host.pointer_left();
            }
        }
        match action {
            Action::None => {}
            Action::Reveal => host.reveal(),
            Action::Tuck => {
                self.away_since = None;
                host.tuck();
            }
        }
    }
}

/// How far below the top edge a drag must have started, px.
const DRAG_PRESS_DEPTH: i32 = 48;
/// A drag must arrive at the edge from at least this far below it...
const DRAG_APPROACH_DEPTH: i32 = 40;
/// ...within this long before touching it.
const DRAG_APPROACH: Duration = Duration::from_millis(700);

/// Tells a drag carried up to the top edge from presses that merely happen
/// to be held there. Fed every tick with how far the pointer is below the
/// top of its monitor (px), whether the left button alone is held, and
/// whether the pointer is at the very top. Pure, so it is tested without a
/// mouse; the cursor and window-move checks are the poller's.
#[derive(Debug, Default)]
pub struct DragGate {
    /// How far below the top the button was first seen held.
    press_depth: Option<i32>,
    /// Recent depths while held, newest last.
    trail: VecDeque<(Instant, i32)>,
    /// The verdict for this visit to the edge, decided on arrival.
    arrived: Option<bool>,
}

impl DragGate {
    /// Takes one reading; true while a drag that came up from below is held
    /// at the edge.
    pub fn sample(&mut self, now: Instant, depth: i32, held: bool, at_top: bool) -> bool {
        if !held {
            *self = DragGate::default();
            return false;
        }
        let press_depth = *self.press_depth.get_or_insert(depth);
        self.trail.push_back((now, depth));
        while self.trail.front().is_some_and(|(t, _)| now.saturating_duration_since(*t) > DRAG_APPROACH) {
            self.trail.pop_front();
        }
        if !at_top {
            self.arrived = None;
            return false;
        }
        *self.arrived.get_or_insert_with(|| {
            press_depth >= DRAG_PRESS_DEPTH && self.trail.iter().any(|&(_, d)| d >= DRAG_APPROACH_DEPTH)
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ms(n: u64) -> Duration {
        Duration::from_millis(n)
    }

    /// Feeds (ms, depth) readings with the button held; returns the last answer.
    fn drag(gate: &mut DragGate, t0: Instant, path: &[(u64, i32)]) -> bool {
        path.iter().map(|&(t, d)| gate.sample(t0 + ms(t), d, true, d < 2)).last().unwrap()
    }

    #[test]
    fn a_drag_brought_up_from_below_opens_the_edge() {
        let t0 = Instant::now();
        let mut gate = DragGate::default();
        assert!(drag(&mut gate, t0, &[(0, 500), (100, 300), (200, 120), (260, 30), (300, 0)]));
        // It stays open while held at the edge, however long.
        assert!(gate.sample(t0 + ms(2000), 0, true, true));
    }

    #[test]
    fn a_press_at_the_top_never_does() {
        // A title bar or a browser tab, pressed and held against the edge.
        let t0 = Instant::now();
        let mut gate = DragGate::default();
        assert!(!drag(&mut gate, t0, &[(0, 20), (100, 60), (200, 0)]));
        assert!(!drag(&mut gate, t0, &[(300, 0), (900, 0)]));
    }

    #[test]
    fn creeping_up_slowly_does_not_count_until_it_comes_up_again() {
        let t0 = Instant::now();
        let mut gate = DragGate::default();
        // Pressed low, but the last 700 ms were spent in the top few pixels.
        assert!(!drag(&mut gate, t0, &[(0, 400), (100, 30), (500, 20), (900, 10), (1000, 0)]));
        // Down and quickly back up: now it is a drag to the edge.
        assert!(drag(&mut gate, t0, &[(1100, 90), (1250, 0)]));
    }

    #[test]
    fn letting_go_starts_over() {
        let t0 = Instant::now();
        let mut gate = DragGate::default();
        assert!(drag(&mut gate, t0, &[(0, 300), (150, 0)]));
        assert!(!gate.sample(t0 + ms(200), 0, false, true));
        // The next press starts at the top: no drag.
        assert!(!drag(&mut gate, t0, &[(300, 0), (400, 0)]));
    }
}
