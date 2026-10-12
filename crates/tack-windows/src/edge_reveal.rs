//! Decides when the board comes down from the top edge and when it tucks
//! away, and flips the window's click-through as the pointer crosses the
//! board. Nothing is polled to do it, which would hold a thread awake, and
//! hooking the mouse would add latency to every app on the system: the
//! poller is told, and sleeps in between.
//!
//! - Tucked, the edge strips ([`crate::edge_strip`]) tell when the pointer
//!   reaches the middle of the top edge, or something carried from another
//!   app does.
//! - Shown, the guard window (the same module) hears the pointer move over,
//!   and leave, the board's zone; the page says when the pointer is off the
//!   board but over the window; the app says when something that held the
//!   board lets go or the board's place changes. Each is one look at the
//!   pointer ([`tack_core::shown`]), and a one-shot wait is asked for only
//!   while something counts down: a peek, or the leave delay.
//!
//! Wake-ups per second:
//!
//! - tucked: 0, edge reveal on or off
//! - tucked, the pointer at the edge, until it leaves or the board comes
//!   down: 40, fewer than that while the reveal dwell is about to end
//! - tucked, something carried to the edge: 1 or 2, until its dwell ends
//! - tucked just now under the pointer at the edge: 10, until it leaves
//! - shown: 0 with the pointer still. One for each input event while it
//!   moves over the board's zone (or something is carried over it), one at
//!   the end of a peek, one at the end of the leave delay.
//! - tucked, the strips could not be made: 10
//! - tucked, the strips could not be made and the pointer unreadable (a UAC
//!   prompt): 1 for three seconds, then 0.1

use std::sync::mpsc::{self, RecvTimeoutError, Sender};
use std::sync::OnceLock;
use std::time::{Duration, Instant};

use tack_core::shown::Shown;
use tack_core::{Settings, View};
use windows::Win32::Foundation::POINT;

use crate::edge_strip::{self, EdgeEvent, EdgeStrips};
use crate::pointer::{self, VK_LBUTTON, VK_RBUTTON};
use crate::{overlay, snipping_tool};

/// Tucked with the pointer at the edge.
const POLL: Duration = Duration::from_millis(25);
/// Tucked without edge strips: a look this often. Nothing tells then that the
/// pointer is at the edge.
const POLL_SLOW: Duration = Duration::from_millis(100);
/// Movement that still counts as resting at the top edge, px. The dwell
/// itself, how long the pointer must rest before revealing, is the user's
/// reveal speed ([`tack_core::RevealSpeed::dwell`]). At the slowest it is
/// long enough that reaching for a title bar or a browser tab, which passes
/// through the edge too, does not count.
const EDGE_JITTER: i32 = 3;
/// How long something carried over the edge waits for the board: the user is
/// waiting with it in hand. At most the edge's dwell: a drag never waits
/// longer than the pointer would.
const DRAG_DWELL: Duration = Duration::from_millis(220);
// The grace period once the pointer is outside the board's zone is the
// user's speed too ([`tack_core::RevealSpeed::leave_delay`]). Everything else
// here is class C (see the policy on `RevealSpeed`): an input threshold, a
// physical gesture's time or polling the system, which never scale.
/// SHQueryUserNotificationState is not free; nobody notices half a second.
const FULLSCREEN_CHECK: Duration = Duration::from_millis(500);
/// The pointer cannot be read: another desktop has the input (a UAC prompt,
/// or the lock screen). Look again this soon, this many times, then this
/// seldom.
const POLL_UNREADABLE: Duration = Duration::from_secs(1);
const UNREADABLE_RETRIES: u32 = 3;
const POLL_UNREADABLE_SLOW: Duration = Duration::from_secs(10);

/// What the poller needs from the app.
pub trait EdgeHost: Send + 'static {
    /// Runs `f` on the shared view, with the settings, under the board lock.
    fn with_view<R>(&self, f: impl FnOnce(&mut View, &Settings) -> R) -> R;
    /// The pointer rested on the top edge: reveal.
    fn reveal(&self);
    fn tuck(&self);
    /// Make the window click-through (`true`) or not.
    fn set_click_through(&self, ignore: bool);
    /// The window just became click-through, so the UI may never see the
    /// pointer leave a print (which would stay in its hover state); tell it.
    fn pointer_left(&self);
    /// The board's window, a raw handle (0 before it exists): the guard goes
    /// just under it.
    fn board_window(&self) -> isize;
}

enum Action {
    None,
    Reveal,
}

/// What the poller remembers between ticks.
struct Poller {
    /// `None`: they could not be made, so the tucked poller looks slowly.
    strips: Option<EdgeStrips>,
    edge: EdgeWatch,
    /// What a shown board remembers of the pointer.
    shown: Shown,
    /// The guard window could not be made, so the pointer's leaving the
    /// board's zone would never be heard.
    guard_failed: bool,
    last_fullscreen_check: Option<Instant>,
    /// Ticks in a row that could not read the pointer.
    unreadable: u32,
}

/// What wakes the poller.
enum Heard {
    /// Something changed: look at once. The board came down or went up, edge
    /// reveal changed, something holding the board let go, the page says the
    /// pointer is off the board.
    Wake,
    Edge(EdgeEvent),
}

/// The way to the poller's thread, for [`wake`].
static POLLER: OnceLock<Sender<Heard>> = OnceLock::new();

/// Starts the poller on a thread of its own.
pub fn start<H: EdgeHost>(host: H) {
    let (tx, rx) = mpsc::channel();
    let _ = POLLER.set(tx.clone());
    std::thread::Builder::new()
        .name("tack-pointer".into())
        .spawn(move || {
            let strips = EdgeStrips::start(move |event| {
                let _ = tx.send(Heard::Edge(event));
            })
            .inspect_err(|e| eprintln!("tack: no edge strips ({e}); looking for the top edge by polling"))
            .ok();
            let mut p = Poller {
                strips,
                edge: EdgeWatch::default(),
                shown: Shown::default(),
                guard_failed: false,
                last_fullscreen_check: None,
                unreadable: 0,
            };
            let mut wait = p.tick(&host);
            loop {
                // `None`: nothing to watch until something is heard.
                let heard = match wait {
                    Some(pause) => rx.recv_timeout(pause),
                    None => rx.recv().map_err(|_| RecvTimeoutError::Disconnected),
                };
                match heard {
                    Ok(Heard::Edge(EdgeEvent::GuardFailed)) => p.guard_failed = true,
                    Ok(Heard::Edge(event)) => p.edge.hear(event, Instant::now()),
                    Ok(Heard::Wake) | Err(RecvTimeoutError::Timeout) => {}
                    Err(RecvTimeoutError::Disconnected) => return,
                }
                wait = p.tick(&host);
            }
        })
        .expect("pointer thread");
}

/// Something changed that a look at the pointer may find a decision in: the
/// board just came down or went up, edge reveal came on or off, something
/// that held the board let go, the board's rect moved, or the page says the
/// pointer is off the board. The poller looks at once rather than at its next
/// (perhaps parked) tick, so click-through follows the pointer from the start
/// and the edge strips and the guard follow the board.
pub fn wake() {
    if let Some(poller) = POLLER.get() {
        let _ = poller.send(Heard::Wake);
    }
}

impl Poller {
    /// Nothing is being watched: forget what a watch builds up.
    fn forget(&mut self) {
        self.edge.reset();
        // Asked afresh, when there is a reason to again.
        self.last_fullscreen_check = None;
    }

    /// Windows' word on whether a full-screen app is in front (and whether it
    /// is Snipping Tool's own overlay), if it is due: asking costs a little.
    fn fullscreen_reading(&mut self, now: Instant) -> Option<(bool, bool)> {
        if self.last_fullscreen_check.is_some_and(|t| now - t < FULLSCREEN_CHECK) {
            return None;
        }
        self.last_fullscreen_check = Some(now);
        let fs = overlay::fullscreen_app_in_front();
        Some((fs, fs && snipping_tool::in_front()))
    }

    /// A look at the board, tucked or shown; returns how long to wait for the
    /// next, or `None` to wait to be told.
    fn tick(&mut self, host: &impl EdgeHost) -> Option<Duration> {
        let now = Instant::now();
        let (shown, edge_reveal) = host.with_view(|v, settings| {
            if !v.shown {
                v.drag_in = false;
            }
            (v.shown, settings.edge_reveal)
        });
        // The strips are there for a tucked board and edge reveal on, no more.
        if let Some(strips) = &mut self.strips {
            strips.set_enabled(!shown && edge_reveal);
            self.edge.listen(strips.enabled());
        }
        if shown {
            return self.look(host, now);
        }

        // Tucked: the guard goes with the board, and so does what a shown
        // board builds up.
        self.shown = Shown::default();
        self.guard_failed = false;
        if let Some(strips) = &mut self.strips {
            strips.set_guard(None, 0, false);
        }
        if !edge_reveal {
            self.forget();
            return None;
        }

        let Some(pt) = pointer::cursor_pos() else {
            // Not (0, 0), which would read as the top edge.
            self.forget();
            if self.strips.is_some() {
                // The strips say when it is back.
                return None;
            }
            self.unreadable = self.unreadable.saturating_add(1);
            return Some(if self.unreadable <= UNREADABLE_RETRIES { POLL_UNREADABLE } else { POLL_UNREADABLE_SLOW });
        };
        self.unreadable = 0;
        let left = pointer::key_down(VK_LBUTTON.0);
        let right = pointer::key_down(VK_RBUTTON.0);
        let zone = edge_strip::zone_at(pt);

        // (full screen, and it is Snipping Tool's own overlay.) Only the
        // pointer at the edge, where it would reveal, needs to know.
        let fullscreen = if zone.contains(pt.x, pt.y) {
            self.fullscreen_reading(now)
        } else {
            // So the edge asks at once when the pointer gets there.
            self.last_fullscreen_check = None;
            None
        };

        let mut pause = None;
        let action = host.with_view(|v, settings| {
            if v.shown {
                // Revealed since: the reveal wakes the poller.
                return Action::None;
            }
            let edge_dwell = settings.reveal.dwell();
            if let Some((fs, snipping)) = fullscreen {
                v.fullscreen = fs;
                v.snipping = snipping;
            }
            let at_top = zone.contains(pt.x, pt.y);
            if !at_top {
                v.edge_armed = true;
            }
            let free = !left && !right;
            let action = if !settings.edge_reveal || !at_top || !v.edge_armed || v.fullscreen {
                self.edge.reset();
                Action::None
            } else {
                match self.edge.rested(now, pt, free, edge_dwell) {
                    Rest::No => Action::None,
                    Rest::Edge => Action::Reveal,
                    Rest::Drag => {
                        v.drag_in = true;
                        Action::Reveal
                    }
                }
            };
            pause =
                tucked_wait(self.strips.is_some(), self.edge.next_look(now, edge_dwell, v.edge_armed, v.fullscreen));
            action
        });

        match action {
            Action::None => pause,
            Action::Reveal => {
                host.reveal();
                Some(POLL)
            }
        }
    }

    /// One look at a shown board: the pointer decides whether it tucks, whether
    /// the window takes clicks and where the guard goes; the wait is until
    /// something counting down ends.
    fn look(&mut self, host: &impl EdgeHost, now: Instant) -> Option<Duration> {
        // Not under the board lock: reading the pointer may involve other
        // windows.
        let pt = pointer::cursor_pos();
        let left = pointer::key_down(VK_LBUTTON.0);
        let fullscreen = self.fullscreen_reading(now);
        // Without the guard nothing would say when the pointer leaves.
        let watched = self.strips.is_some() && !self.guard_failed;

        let step = host.with_view(|v, settings| {
            if let Some((fs, snipping)) = fullscreen {
                v.fullscreen = fs;
                v.snipping = snipping;
            }
            let delay = settings.reveal.leave_delay();
            self.shown.look(v, pt.map(|p| (p.x, p.y)), left, now, delay, watched)
        });

        if let Some(ignore) = step.click_through {
            host.set_click_through(ignore);
            if ignore {
                host.pointer_left();
            }
        }
        if let Some(strips) = &mut self.strips {
            // Click-through now: the guard is the one to hear the pointer.
            strips.set_guard(step.zone, host.board_window(), step.click_through == Some(true));
        }
        if step.tuck {
            // The tuck wakes the poller, which takes the guard down.
            host.tuck();
        }
        step.wait
    }
}

/// How long a tucked poller waits: as long as the watch says when the strips
/// tell it about the edge, else a slow look at a time.
fn tucked_wait(strips: bool, next_look: Option<Duration>) -> Option<Duration> {
    if strips {
        next_look
    } else {
        Some(POLL_SLOW)
    }
}

/// What a tucked board knows of the pointer at the top edge: what the strips
/// said, and how long it has rested there. Pure, so the timing is tested
/// without a mouse.
#[derive(Default)]
struct EdgeWatch {
    /// The strips are up: what they say counts.
    listening: bool,
    /// A strip has the pointer, until it says it left.
    arrived: bool,
    /// Something carried with a button held came over a strip, at this time.
    drag: Option<Instant>,
    /// Since when, and where, the pointer has rested at the edge.
    rest: Option<(Instant, POINT)>,
}

/// What a look at the pointer at the edge makes of it.
#[derive(Debug, PartialEq, Eq)]
enum Rest {
    No,
    /// Rested for the dwell: reveal.
    Edge,
    /// Held over the edge for the drag's dwell: reveal for the drop.
    Drag,
}

impl EdgeWatch {
    /// The strips come up (or go): nothing from the last time is true.
    fn listen(&mut self, on: bool) {
        if on {
            self.listening = true;
        } else {
            *self = EdgeWatch::default();
        }
    }

    fn hear(&mut self, event: EdgeEvent, now: Instant) {
        // Said by a strip just taken down.
        if !self.listening {
            return;
        }
        match event {
            EdgeEvent::Arrived => self.arrived = true,
            EdgeEvent::Left => {
                self.arrived = false;
                self.rest = None;
            }
            EdgeEvent::DragEntered => self.drag = Some(now),
            EdgeEvent::DragLeft => self.drag = None,
            // The guard's only wake the poller: a shown board looks at the
            // pointer, not at the edge.
            EdgeEvent::GuardMoved | EdgeEvent::GuardLeft | EdgeEvent::GuardFailed => {}
        }
    }

    /// Not at the edge, or not to be revealed from: start over.
    fn reset(&mut self) {
        self.rest = None;
        self.drag = None;
    }

    /// One look with the pointer at the edge and nothing against a reveal.
    /// `free`: no button is held. The rest is counted from the first look,
    /// within [`EDGE_JITTER`] of where it was, and the pointer starts over
    /// after a reveal. A drag waits a dwell of its own, wherever it moves
    /// along the strip: it left it, if it left the edge.
    fn rested(&mut self, now: Instant, pt: POINT, free: bool, dwell: Duration) -> Rest {
        if let Some(since) = self.drag {
            if now - since < DRAG_DWELL.min(dwell) {
                return Rest::No;
            }
            self.reset();
            return Rest::Drag;
        }
        if !free {
            self.rest = None;
            return Rest::No;
        }
        match self.rest {
            Some((since, anchor)) if (pt.x - anchor.x).abs() < EDGE_JITTER && (pt.y - anchor.y).abs() < EDGE_JITTER => {
                if now - since < dwell {
                    return Rest::No;
                }
                self.reset();
                Rest::Edge
            }
            _ => {
                self.rest = Some((now, pt));
                Rest::No
            }
        }
    }

    /// When a strip-woken poller must look again, or `None` to be told. At
    /// the edge it looks every [`POLL`], and at the end of the dwell, to the
    /// millisecond. A board just tucked under the pointer (not `armed`) looks
    /// slowly for it to leave, as no strip may have seen it come; a
    /// full-screen app is asked about every so often.
    fn next_look(&self, now: Instant, dwell: Duration, armed: bool, fullscreen: bool) -> Option<Duration> {
        if !armed {
            return Some(POLL_SLOW);
        }
        if !(self.arrived || self.drag.is_some()) {
            return None;
        }
        if fullscreen {
            return Some(FULLSCREEN_CHECK);
        }
        let at_edge = self.arrived.then(|| match self.rest {
            Some((since, _)) => dwell.saturating_sub(now - since).min(POLL),
            None => POLL,
        });
        let dragged = self.drag.map(|since| DRAG_DWELL.min(dwell).saturating_sub(now - since));
        at_edge.into_iter().chain(dragged).min()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tack_core::RevealSpeed;

    fn ms(n: u64) -> Duration {
        Duration::from_millis(n)
    }

    const SPEEDS: [RevealSpeed; 3] = [RevealSpeed::Instant, RevealSpeed::Normal, RevealSpeed::Slow];
    const AT_EDGE: POINT = POINT { x: 800, y: 0 };

    fn listening() -> (EdgeWatch, Instant) {
        let mut watch = EdgeWatch::default();
        watch.listen(true);
        (watch, Instant::now())
    }

    /// Plays the poller against a pointer that arrives at the edge and rests:
    /// looks when the watch says, and returns when it reveals, in ms.
    fn reveal_at(speed: RevealSpeed) -> u64 {
        let (mut watch, t0) = listening();
        let dwell = speed.dwell();
        watch.hear(EdgeEvent::Arrived, t0);
        let mut t = 0;
        loop {
            let now = t0 + ms(t);
            if watch.rested(now, AT_EDGE, true, dwell) == Rest::Edge {
                return t;
            }
            t += watch.next_look(now, dwell, true, false).expect("looks while at the edge").as_millis() as u64;
        }
    }

    #[test]
    fn a_pointer_resting_at_the_edge_reveals_at_the_end_of_the_dwell_whatever_the_speed() {
        for speed in SPEEDS {
            assert_eq!(reveal_at(speed), speed.dwell().as_millis() as u64, "{speed:?}");
        }
    }

    #[test]
    fn a_pointer_that_leaves_before_the_dwell_reveals_nothing() {
        let (mut watch, t0) = listening();
        let dwell = RevealSpeed::Slow.dwell();
        watch.hear(EdgeEvent::Arrived, t0);
        assert_eq!(watch.rested(t0, AT_EDGE, true, dwell), Rest::No);
        watch.hear(EdgeEvent::Left, t0 + dwell - ms(1));
        // Nothing to look at until it is back; then the count starts afresh.
        assert_eq!(watch.next_look(t0 + dwell, dwell, true, false), None);
        watch.hear(EdgeEvent::Arrived, t0 + dwell);
        assert_eq!(watch.rested(t0 + dwell, AT_EDGE, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell + dwell - ms(1), AT_EDGE, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell + dwell, AT_EDGE, true, dwell), Rest::Edge);
    }

    #[test]
    fn jitter_keeps_the_count_and_a_move_starts_it_again() {
        let (mut watch, t0) = listening();
        let dwell = RevealSpeed::Normal.dwell();
        let nudged = POINT { x: AT_EDGE.x + EDGE_JITTER - 1, y: 1 };
        let moved = POINT { x: AT_EDGE.x + EDGE_JITTER, y: 0 };
        assert_eq!(watch.rested(t0, AT_EDGE, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell - ms(1), nudged, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell, nudged, true, dwell), Rest::Edge);
        // Starts over after a reveal, and when the pointer moves on.
        assert_eq!(watch.rested(t0 + dwell + ms(10), AT_EDGE, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell + ms(20), moved, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell + ms(20) + dwell - ms(1), moved, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell + ms(20) + dwell, moved, true, dwell), Rest::Edge);
    }

    #[test]
    fn a_held_button_is_no_rest() {
        let (mut watch, t0) = listening();
        let dwell = RevealSpeed::Instant.dwell();
        assert_eq!(watch.rested(t0, AT_EDGE, true, dwell), Rest::No);
        // A press resets the count; letting go counts afresh.
        assert_eq!(watch.rested(t0 + ms(30), AT_EDGE, false, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell, AT_EDGE, true, dwell), Rest::No);
        assert_eq!(watch.rested(t0 + dwell + dwell, AT_EDGE, true, dwell), Rest::Edge);
    }

    #[test]
    fn a_quicker_speed_reveals_sooner() {
        let times = SPEEDS.map(reveal_at);
        assert!(times[0] < times[1] && times[1] < times[2], "{times:?}");
    }

    #[test]
    fn a_drag_held_over_the_edge_reveals_after_its_dwell_or_the_speeds_if_shorter() {
        for speed in SPEEDS {
            let (mut watch, t0) = listening();
            let dwell = speed.dwell();
            let wait = DRAG_DWELL.min(dwell);
            watch.hear(EdgeEvent::DragEntered, t0);
            // The button is down: a drag is not an edge rest.
            assert_eq!(watch.rested(t0 + wait - ms(1), AT_EDGE, false, dwell), Rest::No, "{speed:?}");
            assert_eq!(watch.rested(t0 + wait, AT_EDGE, false, dwell), Rest::Drag, "{speed:?}");
        }
        assert!(DRAG_DWELL < RevealSpeed::Slow.dwell() && RevealSpeed::Instant.dwell() < DRAG_DWELL);
    }

    #[test]
    fn a_drag_that_leaves_the_edge_is_forgotten() {
        let (mut watch, t0) = listening();
        let dwell = RevealSpeed::Slow.dwell();
        watch.hear(EdgeEvent::DragEntered, t0);
        watch.hear(EdgeEvent::DragLeft, t0 + ms(100));
        assert_eq!(watch.rested(t0 + DRAG_DWELL, AT_EDGE, false, dwell), Rest::No);
        assert_eq!(watch.next_look(t0 + DRAG_DWELL, dwell, true, false), None);
        // Or the look finds the pointer gone from the edge (a strip taken
        // down mid-drag): the poller resets it.
        watch.hear(EdgeEvent::DragEntered, t0 + ms(300));
        watch.reset();
        assert_eq!(watch.rested(t0 + ms(300) + DRAG_DWELL, AT_EDGE, false, dwell), Rest::No);
    }

    #[test]
    fn a_drag_is_looked_for_at_the_end_of_its_dwell() {
        let (mut watch, t0) = listening();
        let dwell = RevealSpeed::Slow.dwell();
        watch.hear(EdgeEvent::DragEntered, t0);
        assert_eq!(watch.next_look(t0, dwell, true, false), Some(DRAG_DWELL));
        assert_eq!(watch.next_look(t0 + ms(200), dwell, true, false), Some(ms(20)));
    }

    #[test]
    fn nothing_is_looked_at_until_the_strips_say_so() {
        let (mut watch, t0) = listening();
        let dwell = RevealSpeed::Slow.dwell();
        assert_eq!(watch.next_look(t0, dwell, true, false), None);
        watch.hear(EdgeEvent::Arrived, t0);
        assert_eq!(watch.next_look(t0, dwell, true, false), Some(POLL));
        // A board just tucked under the pointer looks slowly for it to leave.
        assert_eq!(watch.next_look(t0, dwell, false, false), Some(POLL_SLOW));
        // A full-screen app is asked about, not the pointer.
        assert_eq!(watch.next_look(t0, dwell, true, true), Some(FULLSCREEN_CHECK));
        watch.hear(EdgeEvent::Left, t0 + ms(500));
        assert_eq!(watch.next_look(t0 + ms(500), dwell, true, false), None);
    }

    #[test]
    fn strips_that_are_down_are_not_listened_to() {
        let (mut watch, t0) = listening();
        watch.hear(EdgeEvent::Arrived, t0);
        watch.hear(EdgeEvent::DragEntered, t0);
        // The board came down: whatever the strips had said is gone, and what
        // they say on their way out is not true.
        watch.listen(false);
        watch.hear(EdgeEvent::Arrived, t0);
        watch.hear(EdgeEvent::DragEntered, t0);
        assert_eq!(watch.next_look(t0, RevealSpeed::Slow.dwell(), true, false), None);
        assert!(!watch.arrived && watch.drag.is_none());
        // Up again: heard again, from nothing.
        watch.listen(true);
        assert_eq!(watch.next_look(t0, RevealSpeed::Slow.dwell(), true, false), None);
        watch.hear(EdgeEvent::Arrived, t0);
        assert_eq!(watch.next_look(t0, RevealSpeed::Slow.dwell(), true, false), Some(POLL));
    }

    #[test]
    fn without_strips_a_tucked_poller_looks_slowly_and_with_them_when_told() {
        assert_eq!(tucked_wait(false, None), Some(POLL_SLOW));
        assert_eq!(tucked_wait(false, Some(POLL)), Some(POLL_SLOW));
        assert_eq!(tucked_wait(true, None), None);
        assert_eq!(tucked_wait(true, Some(POLL)), Some(POLL));
    }

    #[test]
    fn at_the_edge_a_look_is_never_oftener_than_needed_nor_later_than_the_dwell_end() {
        let (mut watch, t0) = listening();
        let dwell = RevealSpeed::Slow.dwell();
        watch.hear(EdgeEvent::Arrived, t0);
        assert_eq!(watch.rested(t0, AT_EDGE, true, dwell), Rest::No);
        assert_eq!(watch.next_look(t0, dwell, true, false), Some(POLL));
        assert_eq!(watch.next_look(t0 + dwell - ms(5), dwell, true, false), Some(ms(5)));
    }
}
