//! The user's "Speed" setting: how quickly the board moves and how long it
//! waits, as one choice.
//!
//! One choice sets three things: how long the pointer must rest on the top
//! edge before the board reveals ([`RevealSpeed::dwell`], used by the edge
//! poller), how much faster every animation plays ([`RevealSpeed::motion_scale`],
//! shipped to the UI) and the holds and waits around them (the `peek`,
//! `flight_peek`, `notice_peek`, `leave_delay` and `tuck_hide_delay`
//! methods below).
//!
//! # Which timers follow the speed
//!
//! Every timer in the app is one of these, and Normal is always the original
//! value, so Normal behaves exactly as before the setting existed:
//!
//! * **A, animation**: scales with [`RevealSpeed::motion_scale`]
//!   ([`RevealSpeed::scaled`]).
//! * **B, hold, peek or wait after an event**: scales too, but with a floor
//!   so the result stays readable ([`RevealSpeed::scaled_min`]).
//! * **C, never scales**: an input threshold, the time a physical gesture
//!   takes, polling the system, or a delay that exists for correctness (the
//!   clipboard settling, a file finishing its write, a web view that must be
//!   awake first). The edge dwell has its own table ([`RevealSpeed::dwell`]).
//! * **D, reading time**: text the user must read keeps the time it needs.
//!   The one-time tip stays at a fixed nine seconds; a notice keeps its
//!   reading time and only gets shorter by what the quicker swing saves
//!   ([`RevealSpeed::notice_peek`]).
//! * **E, accessibility**: `prefers-reduced-motion` is the UI's job.

use std::time::Duration;

use serde::de::IgnoredAny;
use serde::{Deserialize, Deserializer, Serialize};

/// The UI's tuck slide at Normal, ms. Mirrors `--dur-tuck` in
/// ui/styles/tokens.css and `TUCK_MS` in ui/scripts/reveal.js, which are kept
/// equal to this by hand.
pub const TUCK_MS: u64 = 190;
/// Time after the tuck slide for the page's last frame to be drawn before the
/// window is hidden. Class C (correctness): it does not shrink with the speed.
pub const TUCK_MARGIN: Duration = Duration::from_millis(110);

const PEEK: Duration = Duration::from_millis(3000);
const PEEK_FLOOR: Duration = Duration::from_millis(1200);
const FLIGHT_PEEK: Duration = Duration::from_millis(1750);
const FLIGHT_PEEK_FLOOR: Duration = Duration::from_millis(800);
const NOTICE_PEEK: Duration = Duration::from_millis(1600);
/// The UI's reveal swing at Normal: what a faster speed saves a notice.
const NOTICE_SWING: Duration = Duration::from_millis(600);
const LEAVE_DELAY: Duration = Duration::from_millis(250);
const LEAVE_DELAY_FLOOR: Duration = Duration::from_millis(120);

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum RevealSpeed {
    /// Almost at once: the pointer need hardly rest.
    Instant,
    Fast,
    /// Long enough that passing through the edge (to a title bar or a
    /// browser tab) does not count.
    #[default]
    Normal,
}

impl RevealSpeed {
    /// How long the pointer must rest on the top edge before the board
    /// reveals.
    pub fn dwell(self) -> Duration {
        Duration::from_millis(match self {
            RevealSpeed::Instant => 100,
            RevealSpeed::Fast => 250,
            RevealSpeed::Normal => 400,
        })
    }

    /// What the UI multiplies every animation's duration by, so a quicker
    /// setting also moves quicker; 1 is the original pace. The one number the
    /// rest of this type is built on.
    pub fn motion_scale(self) -> f64 {
        match self {
            RevealSpeed::Instant => 0.35,
            RevealSpeed::Fast => 0.65,
            RevealSpeed::Normal => 1.0,
        }
    }

    /// `d` at this speed (class A, no floor), to whole milliseconds and never
    /// below 1 ms. Normal is `d` itself.
    pub fn scaled(self, d: Duration) -> Duration {
        let scale = self.motion_scale();
        if scale == 1.0 {
            return d;
        }
        let ms = (d.as_secs_f64() * 1000.0 * scale).round().max(1.0);
        Duration::from_millis(ms as u64)
    }

    /// `d` at this speed but not below `floor` (class B), and never above `d`
    /// itself: Normal is `d` exactly, even for a `floor` longer than `d`.
    pub fn scaled_min(self, d: Duration, floor: Duration) -> Duration {
        self.scaled(d).max(floor).min(d)
    }

    /// How long a new screenshot keeps the board down on its own. Class B.
    /// Normal 3000 ms, never under 1200 ms.
    pub fn peek(self) -> Duration {
        self.scaled_min(PEEK, PEEK_FLOOR)
    }

    /// The same for a capture that flies in. Class B. Normal 1750 ms, never
    /// under 800 ms: the UI's flight takes about 890 ms at Normal (times
    /// [`motion_scale`](Self::motion_scale)) once the window shows, and the
    /// pin's landing must stay in view before the board rests and goes.
    pub fn flight_peek(self) -> Duration {
        self.scaled_min(FLIGHT_PEEK, FLIGHT_PEEK_FLOOR)
    }

    /// How long the board stays down for a notice alone. Class D: the text
    /// has to be read, so only what the quicker swing saves comes off (the
    /// UI's reveal swing is 600 ms at Normal). Normal 1600 ms, Fast 1390 ms,
    /// Instant 1210 ms.
    pub fn notice_peek(self) -> Duration {
        NOTICE_PEEK - (NOTICE_SWING - self.scaled(NOTICE_SWING))
    }

    /// The grace the pointer poller gives once the pointer is outside the
    /// board's zone, so a brief overshoot does not tuck it. Class B. Normal
    /// 250 ms, never under 120 ms.
    pub fn leave_delay(self) -> Duration {
        self.scaled_min(LEAVE_DELAY, LEAVE_DELAY_FLOOR)
    }

    /// How long after `board:tuck` the window may hide: the UI's tuck slide
    /// at this speed plus [`TUCK_MARGIN`]. Class A plus a correctness margin.
    /// Normal 300 ms.
    pub fn tuck_hide_delay(self) -> Duration {
        self.scaled(Duration::from_millis(TUCK_MS)) + TUCK_MARGIN
    }

    /// "instant", "fast" or "normal", exactly; anything else is `None`.
    pub fn parse(text: &str) -> Option<RevealSpeed> {
        match text {
            "instant" => Some(RevealSpeed::Instant),
            "fast" => Some(RevealSpeed::Fast),
            "normal" => Some(RevealSpeed::Normal),
            _ => None,
        }
    }
}

/// A saved value that is not a speed (a hand-edited file, a newer Tack's)
/// reads as the default: board.json must still load.
impl<'de> Deserialize<'de> for RevealSpeed {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        #[derive(Deserialize)]
        #[serde(untagged)]
        enum Saved {
            Text(String),
            Other(IgnoredAny),
        }
        Ok(match Saved::deserialize(deserializer)? {
            Saved::Text(text) => RevealSpeed::parse(&text).unwrap_or_default(),
            Saved::Other(_) => RevealSpeed::default(),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const ALL: [RevealSpeed; 3] = [RevealSpeed::Instant, RevealSpeed::Fast, RevealSpeed::Normal];

    fn ms(n: u64) -> Duration {
        Duration::from_millis(n)
    }

    #[test]
    fn normal_is_the_default_and_keeps_the_original_pace() {
        assert_eq!(RevealSpeed::default(), RevealSpeed::Normal);
        assert_eq!(RevealSpeed::Normal.dwell(), ms(400));
        assert_eq!(RevealSpeed::Normal.motion_scale(), 1.0);
    }

    #[test]
    fn normal_waits_exactly_as_it_always_did() {
        let n = RevealSpeed::Normal;
        assert_eq!(n.peek(), ms(3000));
        assert_eq!(n.flight_peek(), ms(1750));
        assert_eq!(n.notice_peek(), ms(1600));
        assert_eq!(n.leave_delay(), ms(250));
        assert_eq!(n.tuck_hide_delay(), ms(300));
        assert_eq!(n.scaled(ms(1234)), ms(1234));
        assert_eq!(n.scaled_min(ms(1234), ms(5000)), ms(1234));
    }

    #[test]
    fn quicker_speeds_wait_less_and_move_quicker() {
        assert_eq!(RevealSpeed::Instant.dwell(), ms(100));
        assert_eq!(RevealSpeed::Fast.dwell(), ms(250));
        for pair in ALL.windows(2) {
            assert!(pair[0].dwell() < pair[1].dwell());
            assert!(pair[0].motion_scale() < pair[1].motion_scale());
        }
        // The presets are clearly different paces, not nearly the same.
        let [instant, fast, normal] = ALL.map(RevealSpeed::motion_scale);
        assert!(fast / instant > 1.5 && normal / fast > 1.4, "{instant} {fast} {normal}");
        assert!(instant > 0.0 && normal == 1.0);
    }

    #[test]
    fn every_wait_shortens_with_the_speed() {
        type Wait = fn(RevealSpeed) -> Duration;
        let waits: [(&str, Wait); 5] = [
            ("peek", RevealSpeed::peek),
            ("flight_peek", RevealSpeed::flight_peek),
            ("notice_peek", RevealSpeed::notice_peek),
            ("leave_delay", RevealSpeed::leave_delay),
            ("tuck_hide_delay", RevealSpeed::tuck_hide_delay),
        ];
        for (name, wait) in waits {
            let [instant, fast, normal] = ALL.map(wait);
            assert!(instant <= fast && fast <= normal, "{name}: {instant:?} {fast:?} {normal:?}");
            assert!(instant < normal, "{name}: Instant must be quicker than Normal");
        }
        // Where no floor interferes they are strictly ordered.
        for wait in [RevealSpeed::peek, RevealSpeed::notice_peek, RevealSpeed::tuck_hide_delay] {
            assert!(wait(RevealSpeed::Instant) < wait(RevealSpeed::Fast));
            assert!(wait(RevealSpeed::Fast) < wait(RevealSpeed::Normal));
        }
    }

    #[test]
    fn a_peek_stays_long_enough_to_see() {
        for speed in ALL {
            assert!(speed.peek() >= PEEK_FLOOR);
            assert!(speed.flight_peek() >= FLIGHT_PEEK_FLOOR);
            assert!(speed.leave_delay() >= LEAVE_DELAY_FLOOR);
            // The capture's landing is seen before its board rests: the UI's
            // flight (about 890 ms at Normal) plus a beat.
            assert!(speed.flight_peek() > speed.scaled(ms(890)), "{speed:?}");
        }
        assert_eq!(RevealSpeed::Instant.peek(), PEEK_FLOOR);
        assert_eq!(RevealSpeed::Instant.flight_peek(), FLIGHT_PEEK_FLOOR);
        assert_eq!(RevealSpeed::Instant.leave_delay(), LEAVE_DELAY_FLOOR);
    }

    #[test]
    fn a_notice_keeps_its_reading_time() {
        // Only what the quicker swing saves comes off.
        for speed in ALL {
            let saved = NOTICE_PEEK - speed.notice_peek();
            assert_eq!(saved, NOTICE_SWING - speed.scaled(NOTICE_SWING), "{speed:?}");
            assert!(speed.notice_peek() > ms(1000), "{speed:?}");
        }
    }

    #[test]
    fn the_window_hides_only_after_the_tuck_slide() {
        for speed in ALL {
            assert!(speed.tuck_hide_delay() >= speed.scaled(ms(TUCK_MS)) + TUCK_MARGIN, "{speed:?}");
            assert!(speed.tuck_hide_delay() > speed.scaled(ms(TUCK_MS)));
        }
    }

    #[test]
    fn scaling_rounds_to_whole_milliseconds_and_never_reaches_zero() {
        for speed in ALL {
            for d in [ms(1), ms(2), ms(3), ms(190), ms(3000)] {
                let s = speed.scaled(d);
                assert!(s >= ms(1), "{speed:?} {d:?}");
                assert_eq!(s.subsec_nanos() % 1_000_000, 0, "whole ms: {s:?}");
                assert!(s <= d);
            }
        }
        assert_eq!(RevealSpeed::Instant.scaled(ms(1)), ms(1));
        // A floor above the duration never lengthens it.
        assert_eq!(RevealSpeed::Instant.scaled_min(ms(100), ms(500)), ms(100));
        assert_eq!(RevealSpeed::Fast.scaled_min(ms(1000), ms(100)), RevealSpeed::Fast.scaled(ms(1000)));
        assert_eq!(RevealSpeed::Instant.scaled_min(ms(1000), ms(600)), ms(600));
    }

    #[test]
    fn it_is_saved_as_a_lowercase_word() {
        for (speed, text) in ALL.into_iter().zip(["instant", "fast", "normal"]) {
            assert_eq!(serde_json::to_string(&speed).unwrap(), format!("\"{text}\""));
            assert_eq!(serde_json::from_str::<RevealSpeed>(&format!("\"{text}\"")).unwrap(), speed);
            assert_eq!(RevealSpeed::parse(text), Some(speed));
        }
    }

    #[test]
    fn anything_else_is_not_a_speed() {
        for bad in ["", "Fast", "FAST", " fast", "turbo", "slow", "0"] {
            assert_eq!(RevealSpeed::parse(bad), None, "{bad:?}");
            let json = serde_json::to_string(bad).unwrap();
            assert_eq!(serde_json::from_str::<RevealSpeed>(&json).unwrap(), RevealSpeed::Normal, "{bad:?}");
        }
        for odd in ["null", "7", "true", "[]", "{\"a\":1}"] {
            assert_eq!(serde_json::from_str::<RevealSpeed>(odd).unwrap(), RevealSpeed::Normal, "{odd}");
        }
    }
}
