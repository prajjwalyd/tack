//! What a shown board does about the pointer: when it tucks, when its window
//! takes clicks and where the guard window goes. Pure, so it is tested
//! without a mouse: a look is the pointer's position and the clock in, a
//! [`Step`] out. Nobody looks on a timer of their own: `tack-windows` takes
//! a look when it hears something (the guard window saw the pointer move or
//! leave, the page says the pointer is off the board, the app changed what
//! holds the board) and when the one-shot wait this module asked for ends.

use std::time::{Duration, Instant};

use crate::view::{Rect, RevealReason, View, STRIP_H};

/// Slack around the board before the pointer counts as gone, CSS px.
pub const LEAVE_MARGIN: f64 = 10.0;

/// What a look decided.
#[derive(Debug, Default, PartialEq)]
pub struct Step {
    /// The window's click-through changes to this.
    pub click_through: Option<bool>,
    pub tuck: bool,
    /// Look again after this, unless something is heard first; `None`: wait
    /// to be told.
    pub wait: Option<Duration>,
    /// Where the guard window goes (screen px): see [`zone`].
    pub zone: Option<Rect>,
}

/// What a shown board remembers between looks.
#[derive(Default)]
pub struct Shown {
    /// The pointer has been outside the zone, and nothing has held the board,
    /// since then.
    away_since: Option<Instant>,
}

/// The board with its tip and [`LEAVE_MARGIN`] around, reaching up to the
/// top of the monitor: the pointer tends to drift back up the way it came.
fn board_zone(v: &View) -> Option<Rect> {
    v.zone_on_screen().map(|r| {
        let z = r.inflate((LEAVE_MARGIN * v.scale).round() as i32);
        let top = z.y.min(v.monitor_top);
        Rect { x: z.x, y: top, w: z.w, h: z.y + z.h - top }
    })
}

/// The top of the window, from the monitor's top edge: where something
/// carried in from another app may be held while it looks for the board.
fn strip(v: &View) -> Rect {
    Rect {
        x: v.window_pos.0,
        y: v.monitor_top,
        w: v.window_size.0,
        h: v.window_pos.1 - v.monitor_top + (STRIP_H * v.scale).round() as i32,
    }
}

/// Where the guard window goes: all of what counts as being with the board
/// (a window the pointer cannot get through, so no wider than it has to be).
/// `None` while the UI has not said where the board is.
pub fn zone(v: &View) -> Option<Rect> {
    let strip = v.drag_in.then(|| strip(v));
    match (board_zone(v), strip) {
        (Some(board), Some(strip)) => Some(board.union(&strip)),
        (board, strip) => board.or(strip),
    }
}

/// The board that took the keyboard has lost it to another window. It goes,
/// unless something holds it: a menu or a drag out, or Settings, which took
/// the focus on purpose. Nor on the taskbar: a click on the tray icon
/// toggles the board itself, and tucking on the way would bounce it back.
pub fn tucks_when_unfocused(v: &View, keyboard: bool, on_taskbar: bool) -> bool {
    v.shown && keyboard && !on_taskbar && !v.settings_held && v.dragging.is_none() && !v.menu_open
}

impl Shown {
    /// One look. `pt`: the pointer (`None`: unreadable, another desktop has
    /// the input), `left`: the left button is down, `watched`: the guard
    /// window is up, so the pointer's leaving the zone will be heard (if it
    /// is not, the pointer counts as away: the board must not wait for news
    /// that cannot come). Updates what the view says of the pointer's visit
    /// and of the click-through.
    pub fn look(
        &mut self,
        v: &mut View,
        pt: Option<(i32, i32)>,
        left: bool,
        now: Instant,
        leave_delay: Duration,
        watched: bool,
    ) -> Step {
        let mut step = Step::default();
        if !left {
            // Dropped (or let go elsewhere): from now on it leaves like any
            // reveal from the edge.
            v.drag_in = false;
        }
        step.zone = zone(v);
        let Some((x, y)) = pt else { return step };

        let on_board = board_zone(v).is_some_and(|b| b.contains(x, y));
        if on_board {
            v.entered = true;
            // Visited: from now on it leaves like any other reveal.
            v.stay_open = false;
        }
        let in_zone = watched && (on_board || (v.drag_in && strip(v).contains(x, y)));

        // The window takes clicks on the board and lets them through
        // elsewhere (it covers the whole work area), but not under a menu,
        // which is not the pointer's to follow. A print dragged out is: the
        // drop loop drops on whatever window is under the pointer.
        let over_board = v.hit(x, y);
        if !v.menu_open && over_board == v.ignoring {
            v.ignoring = !over_board;
            step.click_through = Some(v.ignoring);
        }

        let held = v.dragging.is_some() || v.menu_open;
        // Still waiting to be visited: for a new capture, its peek.
        let peek_left = v
            .peek_until
            .filter(|_| !v.entered)
            .and_then(|end| end.checked_duration_since(now))
            .filter(|d| !d.is_zero());
        // Settings has the focus: the board stays down (no leave, no peek
        // end) for as long as it does.
        let busy = held || v.stay_open || peek_left.is_some() || v.settings_held;

        // A full-screen app coming to the front sends the edge's and a new
        // capture's board back up; Snipping Tool's overlay only the edge's,
        // since a new capture's board may well come down while that overlay
        // is still fading out.
        let fullscreen_tucks = v.fullscreen
            && (match v.reason {
                RevealReason::Edge => true,
                RevealReason::New => !v.snipping,
                RevealReason::Hotkey | RevealReason::Tray | RevealReason::Settings => false,
            }
                // A full-screen app wins over the Settings hold, whoever
                // revealed the board; the hold ends with the tuck (but not
                // for Snipping Tool's own overlay).
                || (v.settings_held && !v.snipping));
        if !held && fullscreen_tucks {
            self.away_since = None;
            step.tuck = true;
        } else if in_zone || busy {
            self.away_since = None;
            step.wait = peek_left;
        } else {
            let since = *self.away_since.get_or_insert(now);
            match leave_delay.checked_sub(now - since).filter(|rest| !rest.is_zero()) {
                Some(rest) => step.wait = Some(rest),
                None => {
                    self.away_since = None;
                    step.tuck = true;
                }
            }
        }
        step
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::RevealSpeed;

    const SPEEDS: [RevealSpeed; 3] = [RevealSpeed::Instant, RevealSpeed::Normal, RevealSpeed::Slow];
    const DELAY: Duration = Duration::from_millis(250);

    fn ms(n: u64) -> Duration {
        Duration::from_millis(n)
    }

    /// A shown board on a 1920 x 1080 monitor at 100%, 500 px wide, revealed
    /// by the edge and already visited, the pointer's click-through off.
    fn shown() -> View {
        View {
            shown: true,
            entered: true,
            window_size: (1920, 1080),
            rect: Some(Rect { x: 710, y: 0, w: 500, h: 160 }),
            ignoring: false,
            ..View::default()
        }
    }

    const ON_BOARD: Option<(i32, i32)> = Some((900, 80));
    const IN_MARGIN: Option<(i32, i32)> = Some((705, 80));
    const AWAY: Option<(i32, i32)> = Some((300, 600));

    fn look(watch: &mut Shown, v: &mut View, pt: Option<(i32, i32)>, at: Instant) -> Step {
        watch.look(v, pt, false, at, DELAY, true)
    }

    #[test]
    fn the_zone_is_the_board_and_its_tip_with_a_margin() {
        let mut v = shown();
        // 10 px around, and above the monitor's top too: nothing is there.
        assert_eq!(zone(&v), Some(Rect { x: 700, y: -10, w: 520, h: 180 }));
        // The tip hangs under the board, and the zone with it.
        v.tip = Some(Rect { x: 800, y: 170, w: 300, h: 60 });
        assert_eq!(zone(&v), Some(Rect { x: 700, y: -10, w: 520, h: 250 }));
        // The UI has not said where the board is.
        v.rect = None;
        v.tip = None;
        assert_eq!(zone(&v), None);
    }

    #[test]
    fn the_margin_and_the_strip_scale_with_the_monitor() {
        for (scale, margin) in [(1.0, 10), (1.5, 15), (2.0, 20)] {
            let v = View { scale, rect: Some(Rect { x: 1000, y: 0, w: 800, h: 320 }), ..shown() };
            let z = zone(&v).unwrap();
            assert_eq!((z.x, z.w, z.h), (1000 - margin, 800 + 2 * margin, 320 + 2 * margin), "{scale}");
        }
        let v = View { scale: 2.0, drag_in: true, ..shown() };
        // The whole width of the window and 176 CSS px of it, at 200%.
        assert_eq!(zone(&v).map(|z| (z.x, z.w, z.y + z.h)), Some((0, 1920, 352)));
    }

    #[test]
    fn the_zone_follows_a_monitor_to_the_left_with_negative_coordinates() {
        // The second monitor sits left of and above the main one, taskbar at
        // the top: its work area starts 48 px under its top.
        let v = View {
            window_pos: (-2560, -312),
            window_size: (2560, 1392),
            monitor_top: -360,
            scale: 2.0,
            rect: Some(Rect { x: 900, y: 0, w: 760, h: 316 }),
            ..shown()
        };
        // The board is at x -1660..-900, y -312..4; with 20 px of margin and
        // the reach up to the monitor's top (over the taskbar):
        assert_eq!(zone(&v), Some(Rect { x: -1680, y: -360, w: 800, h: 384 }));
        let z = zone(&v).unwrap();
        assert!(z.contains(-1300, -360) && z.contains(-1680, 20) && !z.contains(-1681, 20) && !z.contains(-880, 20));
        let drag = View { drag_in: true, ..v };
        let z = zone(&drag).unwrap();
        // The strip: the window's width from the monitor's top, 176 CSS px down.
        assert_eq!((z.x, z.w, z.y, z.y + z.h), (-2560, 2560, -360, 40));
    }

    #[test]
    fn leaving_starts_a_countdown_and_the_timeout_tucks() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        assert_eq!(look(&mut w, &mut v, ON_BOARD, t0), Step { zone: zone(&v), ..Step::default() });
        // Gone: the board waits out the delay, with nothing in between.
        let step = look(&mut w, &mut v, AWAY, t0 + ms(10));
        assert!(!step.tuck);
        assert_eq!(step.wait, Some(DELAY));
        // Told of something else meanwhile: the wait is what is left.
        assert_eq!(look(&mut w, &mut v, AWAY, t0 + ms(110)).wait, Some(ms(150)));
        // The timer ends.
        assert!(look(&mut w, &mut v, AWAY, t0 + ms(10) + DELAY).tuck);
    }

    #[test]
    fn leaving_and_coming_back_cancels() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        assert!(look(&mut w, &mut v, AWAY, t0).wait.is_some());
        // Back in the margin before the delay is up.
        let step = look(&mut w, &mut v, IN_MARGIN, t0 + ms(100));
        assert!(!step.tuck && step.wait.is_none());
        // A timer that was already on its way finds the pointer there.
        assert!(!look(&mut w, &mut v, IN_MARGIN, t0 + DELAY).tuck);
        // Gone again: the whole delay again, not what was left of the first.
        assert_eq!(look(&mut w, &mut v, AWAY, t0 + ms(400)).wait, Some(DELAY));
    }

    #[test]
    fn the_delay_follows_the_speed() {
        for speed in SPEEDS {
            let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
            let delay = speed.leave_delay();
            assert_eq!(w.look(&mut v, AWAY, false, t0, delay, true).wait, Some(delay));
            assert!(!w.look(&mut v, AWAY, false, t0 + delay - ms(1), delay, true).tuck, "{speed:?}");
            assert!(w.look(&mut v, AWAY, false, t0 + delay, delay, true).tuck, "{speed:?}");
        }
        assert!(RevealSpeed::Instant.leave_delay() < RevealSpeed::Normal.leave_delay());
        assert!(RevealSpeed::Normal.leave_delay() < RevealSpeed::Slow.leave_delay());
    }

    #[test]
    fn a_menu_or_a_drag_out_holds_the_board_and_letting_go_starts_the_count() {
        for hold in [|v: &mut View| v.menu_open = true, |v: &mut View| v.dragging = Some("a".into())] {
            let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
            hold(&mut v);
            let step = look(&mut w, &mut v, AWAY, t0);
            assert!(!step.tuck && step.wait.is_none(), "nothing to wait for: the end of the hold is announced");
            // However long it lasts, and even for a full-screen app.
            v.fullscreen = true;
            assert!(!look(&mut w, &mut v, AWAY, t0 + ms(5000)).tuck);
            v.fullscreen = false;
            v.menu_open = false;
            v.dragging = None;
            // Not what the hold had already run: the delay from the end of it.
            assert_eq!(look(&mut w, &mut v, AWAY, t0 + ms(6000)).wait, Some(DELAY));
        }
    }

    #[test]
    fn a_button_held_carrying_something_in_keeps_the_strip_and_letting_go_ends_it() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        v.drag_in = true;
        // Over the top of the window but not the board: still with the board.
        let strip_pt = Some((200, 100));
        let step = w.look(&mut v, strip_pt, true, t0, DELAY, true);
        assert!(!step.tuck && step.wait.is_none() && v.drag_in);
        // Carried away from it: the countdown, as for any pointer.
        assert!(w.look(&mut v, Some((200, 500)), true, t0, DELAY, true).wait.is_some());
        // Dropped on the strip: it is a plain pointer there, away from the board.
        let step = w.look(&mut v, strip_pt, false, t0 + ms(50), DELAY, true);
        assert!(!v.drag_in && step.wait.is_some());
    }

    #[test]
    fn the_peek_holds_until_it_ends_and_the_delay_follows() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        v.entered = false;
        v.reason = RevealReason::New;
        v.peek_until = Some(t0 + ms(3000));
        // Not visited: the board waits for the peek's end, no tuck on the way.
        assert_eq!(look(&mut w, &mut v, AWAY, t0).wait, Some(ms(3000)));
        assert_eq!(look(&mut w, &mut v, AWAY, t0 + ms(2000)).wait, Some(ms(1000)));
        // Then the leave delay, from the end of the peek.
        assert_eq!(look(&mut w, &mut v, AWAY, t0 + ms(3000)).wait, Some(DELAY));
        assert!(look(&mut w, &mut v, AWAY, t0 + ms(3000) + DELAY).tuck);
        // Visited, the peek counts for nothing.
        let (mut w, mut v) = (Shown::default(), View { peek_until: Some(t0 + ms(3000)), ..shown() });
        assert_eq!(look(&mut w, &mut v, AWAY, t0).wait, Some(DELAY));
        // Visited by this very look: the pointer is on the board.
        let (mut w, mut v) = (Shown::default(), View { entered: false, peek_until: Some(t0 + ms(3000)), ..shown() });
        let step = look(&mut w, &mut v, ON_BOARD, t0);
        assert!(v.entered && step.wait.is_none() && !step.tuck);
    }

    #[test]
    fn settings_holds_the_board_and_letting_go_starts_the_count() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        v.settings_held = true;
        let step = look(&mut w, &mut v, AWAY, t0);
        assert!(!step.tuck && step.wait.is_none());
        v.settings_held = false;
        assert_eq!(look(&mut w, &mut v, AWAY, t0 + ms(9000)).wait, Some(DELAY));
        // A full-screen app wins over the hold, unless it is Snipping Tool's overlay.
        v.settings_held = true;
        v.reason = RevealReason::Settings;
        v.fullscreen = true;
        v.snipping = true;
        assert!(!look(&mut w, &mut v, AWAY, t0 + ms(9100)).tuck);
        v.snipping = false;
        assert!(look(&mut w, &mut v, AWAY, t0 + ms(9100)).tuck);
    }

    #[test]
    fn a_shortcut_board_stays_until_the_pointer_has_visited_and_left() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        v.entered = false;
        v.stay_open = true;
        v.reason = RevealReason::Hotkey;
        let step = look(&mut w, &mut v, AWAY, t0);
        assert!(!step.tuck && step.wait.is_none());
        assert!(!look(&mut w, &mut v, AWAY, t0 + ms(60_000)).tuck);
        // Visited: it leaves like any other now.
        look(&mut w, &mut v, ON_BOARD, t0 + ms(61_000));
        assert!(v.entered && !v.stay_open);
        assert!(look(&mut w, &mut v, AWAY, t0 + ms(62_000)).wait.is_some());
    }

    #[test]
    fn losing_the_keyboard_tucks_what_took_it_even_when_it_stays_open() {
        let mut v = shown();
        v.stay_open = true;
        v.reason = RevealReason::Hotkey;
        assert!(tucks_when_unfocused(&v, true, false));
        // A board that never took the keyboard has nothing to lose.
        assert!(!tucks_when_unfocused(&v, false, false));
        // A click on the tray icon is the toggle's.
        assert!(!tucks_when_unfocused(&v, true, true));
        // Holds win: a menu, a drag out, Settings.
        for hold in [
            |v: &mut View| v.menu_open = true,
            |v: &mut View| v.dragging = Some("a".into()),
            |v: &mut View| v.settings_held = true,
        ] {
            let mut v = shown();
            hold(&mut v);
            assert!(!tucks_when_unfocused(&v, true, false));
        }
        assert!(!tucks_when_unfocused(&View::default(), true, false), "already tucked");
    }

    #[test]
    fn the_window_takes_clicks_on_the_board_only() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        v.ignoring = true;
        assert_eq!(look(&mut w, &mut v, ON_BOARD, t0).click_through, Some(false));
        assert!(!v.ignoring);
        // The same again: nothing to change.
        assert_eq!(look(&mut w, &mut v, ON_BOARD, t0).click_through, None);
        // In the margin, off the board: through again, the board still down.
        let step = look(&mut w, &mut v, IN_MARGIN, t0);
        assert_eq!((step.click_through, step.tuck), (Some(true), false));
        // Not under a menu, whatever the pointer does.
        v.menu_open = true;
        assert_eq!(look(&mut w, &mut v, ON_BOARD, t0).click_through, None);
        // A print dragged out follows the pointer.
        v.menu_open = false;
        v.dragging = Some("a".into());
        assert_eq!(look(&mut w, &mut v, ON_BOARD, t0).click_through, Some(false));
    }

    #[test]
    fn a_full_screen_app_tucks_an_edge_or_new_board_but_not_a_shortcut_one() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        v.fullscreen = true;
        assert!(look(&mut w, &mut v, ON_BOARD, t0).tuck);
        v.reason = RevealReason::New;
        assert!(look(&mut w, &mut v, ON_BOARD, t0).tuck);
        // A new capture's board comes down while Snipping Tool's overlay fades.
        v.snipping = true;
        assert!(!look(&mut w, &mut v, ON_BOARD, t0).tuck);
        v.reason = RevealReason::Hotkey;
        v.snipping = false;
        assert!(!look(&mut w, &mut v, ON_BOARD, t0).tuck);
    }

    #[test]
    fn without_the_guard_the_pointer_counts_as_away() {
        // A board whose guard could not be made must not wait for news.
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        let step = w.look(&mut v, ON_BOARD, false, t0, DELAY, false);
        assert_eq!(step.wait, Some(DELAY));
        assert!(w.look(&mut v, ON_BOARD, false, t0 + DELAY, DELAY, false).tuck);
    }

    #[test]
    fn an_unreadable_pointer_changes_nothing() {
        let (mut w, mut v, t0) = (Shown::default(), shown(), Instant::now());
        look(&mut w, &mut v, AWAY, t0);
        let step = look(&mut w, &mut v, None, t0 + ms(100));
        assert_eq!((step.tuck, step.click_through, step.wait), (false, None, None));
        // What was counting is still counting: the next sight of it goes on.
        assert!(look(&mut w, &mut v, AWAY, t0 + DELAY).tuck);
    }
}
