//! Polls the pointer to decide when the board comes down from the top edge
//! and when it tucks away, and flips the window's click-through as the
//! pointer crosses the board. A drag carried from another app to the edge
//! opens it too ([`DragGate`]). A polling thread rather than a global mouse
//! hook, which would add latency to every app on the system. Tucked, it
//! polls less the less can happen. Wake-ups per second:
//!
//! - shown: 40
//! - tucked, the user at the controls (input in the last [`ACTIVE_FOR`]):
//!   25 near the top, 10 elsewhere
//! - tucked, untouched since: one per reveal dwell, 10 / 4 / 2.5 from the
//!   fastest speed to the slowest ([`hidden_pause`])
//! - tucked, edge reveal off: 2
//! - the pointer unreadable (a UAC prompt): 1 for three seconds, then 0.1
//! - session locked or display off: 0, until [`session`] wakes it

use std::collections::VecDeque;
use std::sync::OnceLock;
use std::thread::Thread;
use std::time::{Duration, Instant};

use tack_core::view::STRIP_H;
use tack_core::{Rect, RevealReason, Settings, View};
use windows::Win32::Foundation::POINT;
use windows::Win32::Graphics::Gdi::HMONITOR;

use crate::pointer::{self, VK_LBUTTON, VK_RBUTTON};
use crate::{overlay, session, snipping_tool};

const POLL_SHOWN: Duration = Duration::from_millis(25);
/// Tucked, with the pointer near the top.
const POLL_HIDDEN: Duration = Duration::from_millis(40);
/// Tucked, with the pointer far from the top: getting to the edge takes a
/// moment, and resting there a little more.
const POLL_FAR: Duration = Duration::from_millis(100);
/// Tucked, and the keyboard and mouse were last touched long ago (see
/// [`ACTIVE_FOR`]): one look per reveal dwell, but never oftener than this.
const POLL_RESTING_MIN: Duration = Duration::from_millis(100);
/// The time since the last input within which the user counts as at the
/// controls. Longer than any wait for a drag at the edge ([`DRAG_DWELL`]),
/// which needs a look every tick, and than any reveal dwell.
const ACTIVE_FOR: Duration = Duration::from_secs(2);
/// How far below the monitor top counts as near it, px.
const NEAR_TOP: i32 = 200;
/// Tucked with edge reveal off: nothing to watch for but the setting coming
/// back on, as a reveal wakes the poller ([`wake`]).
const POLL_IDLE: Duration = Duration::from_millis(500);
/// Movement that still counts as resting at the top edge, px. The dwell
/// itself, how long the pointer must rest before revealing, is the user's
/// reveal speed ([`tack_core::RevealSpeed::dwell`]). At the slowest it is
/// long enough that reaching for a title bar or a browser tab, which passes
/// through the edge too, does not count.
const EDGE_JITTER: i32 = 3;
/// The same two for a drag held at the edge: the user is waiting for the
/// board with something in hand, and a hand holding a button wobbles more.
/// The dwell is at most the edge's: a drag never waits longer than the
/// pointer would.
const DRAG_DWELL: Duration = Duration::from_millis(220);
const DRAG_JITTER: i32 = 8;
const _: () = assert!(ACTIVE_FOR.as_millis() > DRAG_DWELL.as_millis());
/// How far below the monitor top still counts as "the very top", px.
const EDGE_BAND: i32 = 2;
// The grace period once the pointer is outside the board's zone is the
// user's speed too ([`tack_core::RevealSpeed::leave_delay`]). Everything else
// here is class C (see the policy on `RevealSpeed`): an input threshold, a
// physical gesture's time or polling the system, which never scale.
/// Slack around the board before the pointer counts as gone, CSS px.
const LEAVE_MARGIN: f64 = 10.0;
/// SHQueryUserNotificationState is not free; nobody notices half a second.
const FULLSCREEN_CHECK: Duration = Duration::from_millis(500);
/// The pointer cannot be read: another desktop has the input (a UAC prompt,
/// or the lock screen where [`session`] did not say). Look again this soon,
/// this many times, then this seldom.
const POLL_UNREADABLE: Duration = Duration::from_secs(1);
const UNREADABLE_RETRIES: u32 = 3;
const POLL_UNREADABLE_SLOW: Duration = Duration::from_secs(10);

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
    /// pointer leave a print (which would stay in its hover state); tell it.
    fn pointer_left(&self);
}

enum Action {
    None,
    Reveal,
    Tuck,
}

/// What the poller remembers between ticks.
struct Poller {
    edge: EdgeRest,
    monitor_top: MonitorTop,
    away_since: Option<Instant>,
    prev_left: bool,
    prev_right: bool,
    /// The left button went down over the board and is still held: a press,
    /// a long press or the start of a drag. Never tuck or flip click-through
    /// under it.
    press_inside: bool,
    last_fullscreen_check: Option<Instant>,
    drag: DragGate,
    /// The last tick did not watch the buttons.
    unwatched: bool,
    /// Ticks in a row that could not read the pointer.
    unreadable: u32,
}

/// The poller's thread, for [`wake`].
static POLLER: OnceLock<Thread> = OnceLock::new();

/// Starts the poller on a thread of its own.
pub fn start<H: EdgeHost>(host: H) {
    std::thread::Builder::new()
        .name("tack-pointer".into())
        .spawn(move || {
            let _ = POLLER.set(std::thread::current());
            let mut p = Poller {
                edge: EdgeRest::default(),
                monitor_top: MonitorTop::default(),
                away_since: None,
                prev_left: false,
                prev_right: false,
                press_inside: false,
                last_fullscreen_check: None,
                drag: DragGate::default(),
                unwatched: false,
                unreadable: 0,
            };
            loop {
                match p.tick(&host) {
                    Some(pause) => std::thread::park_timeout(pause),
                    // Nothing to watch until [`wake`].
                    None => std::thread::park(),
                }
            }
        })
        .expect("pointer thread");
    session::start();
}

/// The board just came down, or edge reveal came on, or the session woke:
/// the poller looks at once rather than at its next (perhaps idle or parked)
/// tick, so click-through follows the pointer from the start.
pub fn wake() {
    if let Some(poller) = POLLER.get() {
        poller.unpark();
    }
}

impl Poller {
    /// Nothing is being watched: forget what a watch builds up.
    fn forget(&mut self, now: Instant) {
        self.edge.reset();
        self.away_since = None;
        self.press_inside = false;
        self.drag.sample(now, 0, false, false);
        // Asked afresh when there is a reason to again.
        self.last_fullscreen_check = None;
        self.unwatched = true;
    }

    /// One look at the pointer; returns how long to wait for the next, or
    /// `None` to wait for [`wake`].
    fn tick(&mut self, host: &impl EdgeHost) -> Option<Duration> {
        let now = Instant::now();
        if session::asleep() {
            self.forget(now);
            return None;
        }
        let (shown, edge_reveal) = host.with_view(|v, settings| {
            if !v.shown {
                v.drag_in = false;
            }
            (v.shown, settings.edge_reveal)
        });
        if !shown && !edge_reveal {
            self.forget(now);
            return Some(POLL_IDLE);
        }

        let Some(pt) = pointer::cursor_pos() else {
            // Not (0, 0), which would read as the top edge.
            self.forget(now);
            self.unreadable = self.unreadable.saturating_add(1);
            return Some(if self.unreadable <= UNREADABLE_RETRIES { POLL_UNREADABLE } else { POLL_UNREADABLE_SLOW });
        };
        self.unreadable = 0;
        let left = pointer::key_down(VK_LBUTTON.0);
        let right = pointer::key_down(VK_RBUTTON.0);
        if std::mem::take(&mut self.unwatched) {
            // Buttons held since an unwatched stretch were not pressed just now.
            self.prev_left = left;
            self.prev_right = right;
        }
        let left_pressed = left && !self.prev_left;
        let right_pressed = right && !self.prev_right;
        self.prev_left = left;
        self.prev_right = right;
        if !left {
            self.press_inside = false;
        }

        // Not under the board lock: WindowFromPoint may send the window
        // under the pointer a message and wait for its answer.
        let on_taskbar = (left_pressed || right_pressed) && pointer::on_taskbar(pt);
        let top = (!shown).then(|| self.monitor_top.at(pt));
        let idle = if shown { Duration::ZERO } else { pointer::input_idle() };

        // (full screen, and it is Snipping Tool's own overlay.) Only a board
        // that is down (a full-screen app tucks it) or the pointer at the
        // edge (where it would reveal) needs to know.
        let wanted = shown || top.is_some_and(|top| pt.y < top + EDGE_BAND);
        let fullscreen = if !wanted {
            // So the edge asks at once when the pointer gets there.
            self.last_fullscreen_check = None;
            None
        } else if self.last_fullscreen_check.is_none_or(|t| now - t >= FULLSCREEN_CHECK) {
            self.last_fullscreen_check = Some(now);
            let fs = overlay::fullscreen_app_in_front();
            Some((fs, fs && snipping_tool::in_front()))
        } else {
            None
        };

        let mut click_through = None;
        let mut pause = POLL_SHOWN;
        let action = host.with_view(|v, settings| {
            let edge_dwell = settings.reveal.dwell();
            let leave_delay = settings.reveal.leave_delay();
            if let Some((fs, snipping)) = fullscreen {
                v.fullscreen = fs;
                v.snipping = snipping;
            }

            if !v.shown {
                self.away_since = None;
                self.press_inside = false;
                v.drag_in = false;
                let top = top.unwrap_or_else(|| self.monitor_top.at(pt));
                let at_top = pt.y < top + EDGE_BAND;
                if !at_top {
                    v.edge_armed = true;
                }
                pause = hidden_pause(idle, pt.y - top < NEAR_TOP, edge_dwell);
                // Something carried here from another app, held over the edge.
                let dragging = self.drag.sample(now, pt.y - top, left && !right, at_top)
                    && pointer::cursor_kind().may_be_dragging()
                    && !pointer::window_moving();
                let buttons_ok = (!left && !right) || dragging;
                let (dwell, jitter) =
                    if dragging { (DRAG_DWELL.min(edge_dwell), DRAG_JITTER) } else { (edge_dwell, EDGE_JITTER) };
                if !settings.edge_reveal || !at_top || !v.edge_armed || !buttons_ok || v.fullscreen {
                    self.edge.reset();
                    Action::None
                } else if self.edge.rested(now, pt, jitter, dwell, idle) {
                    v.drag_in = dragging;
                    Action::Reveal
                } else {
                    Action::None
                }
            } else {
                self.edge.reset();
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
                // Nor in Settings, which counts as outside the board but is
                // what holds it down.
                let clicked_outside = (left_pressed || right_pressed) && !over_board && !on_taskbar && !v.settings_held;

                // The window covers the whole work area, and the drag loop
                // drops on whatever window is under the pointer: off the
                // board it must let a print being dragged out through.
                let follow = v.dragging.is_some() || (!held && !self.press_inside);
                if follow && over_board == v.ignoring {
                    v.ignoring = !over_board;
                    click_through = Some(v.ignoring);
                }

                let peeking = !v.entered && v.peek_until.is_some_and(|t| now < t);
                // Settings has the focus: the board stays down (no leave, no
                // peek end) for as long as it does.
                let busy = held || self.press_inside || v.stay_open || peeking || v.settings_held;

                // A full-screen app coming to the front sends the edge's and a
                // new capture's board back up; Snipping Tool's overlay only
                // the edge's, since a new capture's board may well come down
                // while that overlay is still fading out.
                let fullscreen_tucks = v.fullscreen
                    && (match v.reason {
                        RevealReason::Edge => true,
                        RevealReason::New => !v.snipping,
                        RevealReason::Hotkey | RevealReason::Tray | RevealReason::Settings => false,
                    }
                        // A full-screen app wins over the Settings hold,
                        // whoever revealed the board; the hold ends with the
                        // tuck (but not for Snipping Tool's own overlay).
                        || (v.settings_held && !v.snipping));
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
                    if now - since >= leave_delay {
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
                host.pointer_left();
            }
        }
        Some(match action {
            Action::None => pause,
            Action::Reveal => {
                host.reveal();
                POLL_SHOWN
            }
            Action::Tuck => {
                self.away_since = None;
                host.tuck();
                pause
            }
        })
    }
}

/// How long a tucked poller waits, edge reveal being on. While the user is at
/// the controls: as often as a pointer on its way to the edge needs. Once
/// nothing was touched for [`ACTIVE_FOR`] the pointer is resting wherever it
/// is; a look per dwell is enough, as any input (the pointer coming up to the
/// edge, a press for a drag) first shows as the next look, and from then on
/// the active pace takes over. A pointer that arrives at the edge from there
/// still reveals within one active tick of its dwell ([`EdgeRest::rested`]).
fn hidden_pause(idle: Duration, near_top: bool, dwell: Duration) -> Duration {
    match (idle < ACTIVE_FOR, near_top) {
        (true, true) => POLL_HIDDEN,
        (true, false) => POLL_FAR,
        (false, _) => dwell.max(POLL_RESTING_MIN),
    }
}

/// The pointer resting against the top edge. Pure, so the timing is tested
/// without a mouse.
#[derive(Default)]
struct EdgeRest {
    since: Option<(Instant, POINT)>,
}

impl EdgeRest {
    fn reset(&mut self) {
        self.since = None;
    }

    /// One look with the pointer at the edge and nothing against a reveal;
    /// true once it has rested there for `dwell` (and starts over).
    ///
    /// `idle` is how long ago the user last touched the keyboard or mouse. The
    /// pointer cannot have moved since, so it has rested at least that long,
    /// however seldom it was looked at: a rest of `dwell` is served at once,
    /// which is what lets a poller that sleeps while nothing is touched lose
    /// no time. Otherwise the rest is counted from the first look, within
    /// `jitter` of where it was.
    fn rested(&mut self, now: Instant, pt: POINT, jitter: i32, dwell: Duration, idle: Duration) -> bool {
        let rested = idle >= dwell
            || match self.since {
                Some((since, anchor)) if (pt.x - anchor.x).abs() < jitter && (pt.y - anchor.y).abs() < jitter => {
                    now - since >= dwell
                }
                _ => {
                    self.since = Some((now, pt));
                    false
                }
            };
        if rested {
            self.since = None;
        }
        rested
    }
}

/// The top of the monitor under the pointer. Windows is asked only when the
/// pointer is on another monitor or the displays changed ([`session`]).
#[derive(Default)]
struct MonitorTop {
    known: Option<(HMONITOR, u32)>,
    top: i32,
}

impl MonitorTop {
    fn at(&mut self, pt: POINT) -> i32 {
        let key = (overlay::monitor_of(pt), session::display_epoch());
        if self.known != Some(key) {
            self.top = overlay::top_of(key.0);
            self.known = Some(key);
        }
        self.top
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
pub(crate) struct DragGate {
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
    pub(crate) fn sample(&mut self, now: Instant, depth: i32, held: bool, at_top: bool) -> bool {
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
    use tack_core::RevealSpeed;

    fn ms(n: u64) -> Duration {
        Duration::from_millis(n)
    }

    const SPEEDS: [RevealSpeed; 3] = [RevealSpeed::Instant, RevealSpeed::Fast, RevealSpeed::Normal];
    const AT_EDGE: POINT = POINT { x: 800, y: 0 };

    #[test]
    fn a_tucked_poller_keeps_its_pace_while_the_user_is_at_the_controls() {
        let dwell = RevealSpeed::Normal.dwell();
        assert_eq!(hidden_pause(ms(0), true, dwell), POLL_HIDDEN);
        assert_eq!(hidden_pause(ms(0), false, dwell), POLL_FAR);
        assert_eq!(hidden_pause(ACTIVE_FOR - ms(1), true, dwell), POLL_HIDDEN);
        assert_eq!(hidden_pause(ACTIVE_FOR - ms(1), false, dwell), POLL_FAR);
    }

    #[test]
    fn an_untouched_poller_looks_once_per_dwell_never_oftener_than_a_tenth_of_a_second() {
        for (speed, wait) in SPEEDS.into_iter().zip([100, 250, 400]) {
            for near_top in [true, false] {
                assert_eq!(hidden_pause(ACTIVE_FOR, near_top, speed.dwell()), ms(wait));
            }
        }
        assert_eq!(hidden_pause(ms(60_000), true, ms(40)), POLL_RESTING_MIN);
    }

    #[test]
    fn the_active_window_outlasts_every_wait_a_poll_needs() {
        // Reveal dwells, and a drag at the edge, are served on active ticks.
        for speed in SPEEDS {
            assert!(ACTIVE_FOR > speed.dwell());
        }
        assert!(ACTIVE_FOR > DRAG_DWELL);
    }

    #[test]
    fn a_pointer_untouched_for_the_dwell_has_rested_it() {
        let (t0, dwell) = (Instant::now(), RevealSpeed::Normal.dwell());
        let mut edge = EdgeRest::default();
        // Just short of it: the count starts at this look.
        assert!(!edge.rested(t0, AT_EDGE, EDGE_JITTER, dwell, dwell - ms(1)));
        // The same pointer, untouched for the dwell: no further look needed.
        assert!(edge.rested(t0 + ms(40), AT_EDGE, EDGE_JITTER, dwell, dwell));
        // Starts over after a reveal.
        assert!(!edge.rested(t0 + ms(80), AT_EDGE, EDGE_JITTER, dwell, ms(0)));
    }

    #[test]
    fn a_pointer_just_arrived_counts_from_the_first_look() {
        let (t0, dwell) = (Instant::now(), RevealSpeed::Fast.dwell());
        let mut edge = EdgeRest::default();
        let nudged = POINT { x: AT_EDGE.x + EDGE_JITTER - 1, y: 1 };
        assert!(!edge.rested(t0, AT_EDGE, EDGE_JITTER, dwell, ms(10)));
        // Moving by less than the jitter keeps the count going, however
        // recent the input; the look at the dwell reveals.
        assert!(!edge.rested(t0 + ms(120), nudged, EDGE_JITTER, dwell, ms(5)));
        assert!(!edge.rested(t0 + dwell - ms(1), AT_EDGE, EDGE_JITTER, dwell, ms(5)));
        assert!(edge.rested(t0 + dwell, nudged, EDGE_JITTER, dwell, ms(5)));
    }

    #[test]
    fn a_pointer_that_moves_on_starts_the_count_again() {
        let (t0, dwell) = (Instant::now(), RevealSpeed::Normal.dwell());
        let mut edge = EdgeRest::default();
        let moved = POINT { x: AT_EDGE.x + EDGE_JITTER, y: 0 };
        assert!(!edge.rested(t0, AT_EDGE, EDGE_JITTER, dwell, ms(10)));
        assert!(!edge.rested(t0 + dwell - ms(40), moved, EDGE_JITTER, dwell, ms(10)));
        assert!(!edge.rested(t0 + dwell, moved, EDGE_JITTER, dwell, ms(10)));
        assert!(edge.rested(t0 + dwell - ms(40) + dwell, moved, EDGE_JITTER, dwell, ms(10)));
        // The edge is left: whatever was counted is forgotten.
        edge.reset();
        assert!(!edge.rested(t0 + ms(2000), moved, EDGE_JITTER, dwell, ms(10)));
    }

    /// Plays the tucked poller against a pointer that flicks to the top edge at
    /// `arrival` ms and stays, nothing touched after. Before, the user had last
    /// touched the keyboard or mouse `gap` ms before the arrival. The poller's
    /// first look is at `first` ms. Returns when it reveals.
    fn reveal_at(dwell: Duration, gap: u64, arrival: u64, first: u64) -> u64 {
        let t0 = Instant::now();
        let mut edge = EdgeRest::default();
        let mut t = first;
        loop {
            let at_top = t >= arrival;
            let idle = ms(if at_top { t - arrival } else { (t + gap).saturating_sub(arrival) });
            if at_top && edge.rested(t0 + ms(t), AT_EDGE, EDGE_JITTER, dwell, idle) {
                return t;
            }
            if !at_top {
                edge.reset();
            }
            t += hidden_pause(idle, at_top, dwell).as_millis() as u64;
        }
    }

    #[test]
    fn a_sleeping_poller_still_reveals_within_one_active_tick_of_the_dwell() {
        // Whatever the speed, however long the keyboard and mouse had been
        // left alone, and wherever in its sleep the poller was when the
        // pointer came (every phase): never early, and no later than one tick
        // after the dwell. Today's poller is up to 140 ms after it (a 100 ms
        // far look, a tick for the count, a tick to notice the dwell).
        let arrival = 10_000;
        for speed in SPEEDS {
            let dwell = speed.dwell().as_millis() as u64;
            for gap in [0, 500, 1_999, 2_000, 5_000, 60_000] {
                for first in arrival - 2_500..arrival {
                    let at = reveal_at(speed.dwell(), gap, arrival, first);
                    let (soon, late) = (arrival + dwell, arrival + dwell + POLL_HIDDEN.as_millis() as u64);
                    assert!((soon..=late).contains(&at), "{speed:?}, gap {gap}, first look {first}: {at}");
                }
            }
        }
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
