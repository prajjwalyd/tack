//! Polls the pointer to decide when the board should come down from the top
//! edge and when it should go back up, and flips the window's click-through
//! as the pointer crosses the board's border.
//!
//! A plain polling thread: global mouse hooks would cost every app on the
//! system a little latency. The poller reads and updates the shared
//! [`View`] through an [`EdgeHost`], which also carries out what it decides.

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
                let mon = overlay::monitor_at(pt);
                let at_top = pt.y < mon.monitor.top + EDGE_BAND;
                if !at_top {
                    v.edge_armed = true;
                }
                if !settings.edge_reveal || !at_top || !v.edge_armed || left || right || v.fullscreen {
                    self.hot_since = None;
                    Action::None
                } else {
                    match self.hot_since {
                        Some((since, anchor))
                            if (pt.x - anchor.x).abs() < EDGE_JITTER && (pt.y - anchor.y).abs() < EDGE_JITTER =>
                        {
                            if now - since >= EDGE_DWELL {
                                self.hot_since = None;
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
                let scale = v.scale;
                let board = v.board_on_screen();
                let over_board = board
                    // The UI already adds a small margin around the board.
                    .map(|r| r.contains(pt.x, pt.y))
                    .unwrap_or(false);

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
                } else if !v.entered && v.reason == RevealReason::Edge {
                    // Fresh from the edge the pointer may be anywhere along
                    // the top; the whole strip counts until it finds the board.
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
