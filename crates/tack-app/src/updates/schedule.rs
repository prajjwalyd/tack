//! When Tack looks for updates by itself: a pure function of the clock and
//! the updater's state, so the policy is tested without waiting a day.
//!
//! The first check comes shortly after Tack starts (so a start at login does
//! not compete with everything else starting), then one every 24 hours, and
//! only while the setting is on. A failed check waits for the next turn like
//! a good one does. A check the person asks for is not governed by this.

use std::time::Duration;

use super::status::Phase;

/// After Tack starts.
pub const FIRST_CHECK_DELAY: Duration = Duration::from_secs(45);
/// After a check starts.
pub const CHECK_INTERVAL: Duration = Duration::from_secs(24 * 60 * 60);
/// The longest sleep. The wall clock moves while the PC is suspended or its
/// time is changed, so what is due is worked out again at least this often.
pub const MAX_SLEEP: Duration = Duration::from_secs(60 * 60);
/// How long to wait for a check or download that is already going.
pub const BUSY_WAIT: Duration = Duration::from_secs(30);

/// Everything the policy looks at.
pub struct Inputs {
    /// `settings.updates.check`.
    pub enabled: bool,
    pub phase: Phase,
    /// The wall clock now and when Tack started, in ms since the Unix epoch.
    pub now_ms: u64,
    pub started_ms: u64,
    /// When a check last started (whatever came of it), if one has.
    pub last_attempt_ms: Option<u64>,
}

#[derive(PartialEq, Eq, Debug)]
pub enum Step {
    CheckNow,
    /// Sleep this long, or until woken (the setting changed), then look again.
    Sleep(Duration),
}

pub fn next_step(i: &Inputs) -> Step {
    if !i.enabled {
        // The setting's change wakes the checker; nothing to poll for.
        return Step::Sleep(MAX_SLEEP);
    }
    match i.phase {
        // Never over an install, and no reason to look for one more version
        // while one waits to be installed.
        Phase::Installing | Phase::Available => return Step::Sleep(MAX_SLEEP),
        Phase::Checking | Phase::Downloading => return Step::Sleep(BUSY_WAIT),
        Phase::Idle | Phase::UpToDate | Phase::Error => {}
    }
    let due = match i.last_attempt_ms {
        // The clock was set back past the last check: it says nothing
        // about when the next is due, so check once and start again from now.
        Some(last) if last > i.now_ms => return Step::CheckNow,
        Some(last) => last.saturating_add(CHECK_INTERVAL.as_millis() as u64),
        None => i.started_ms.saturating_add(FIRST_CHECK_DELAY.as_millis() as u64),
    };
    match due.checked_sub(i.now_ms) {
        None | Some(0) => Step::CheckNow,
        Some(wait) => Step::Sleep(Duration::from_millis(wait).min(MAX_SLEEP)),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const START: u64 = 1_000_000;

    fn inputs(now_ms: u64, last_attempt_ms: Option<u64>) -> Inputs {
        Inputs { enabled: true, phase: Phase::Idle, now_ms, started_ms: START, last_attempt_ms }
    }

    fn secs(s: u64) -> u64 {
        s * 1000
    }

    #[test]
    fn the_first_check_is_45_seconds_after_start() {
        assert_eq!(next_step(&inputs(START, None)), Step::Sleep(Duration::from_secs(45)));
        assert_eq!(next_step(&inputs(START + secs(30), None)), Step::Sleep(Duration::from_secs(15)));
        assert_eq!(next_step(&inputs(START + secs(45), None)), Step::CheckNow);
        assert_eq!(next_step(&inputs(START + secs(500), None)), Step::CheckNow);
    }

    #[test]
    fn the_next_checks_are_a_day_apart() {
        let last = START + secs(45);
        // Hours from the check: one sleep, capped.
        assert_eq!(next_step(&inputs(last + secs(10), Some(last))), Step::Sleep(MAX_SLEEP));
        // Close to the day: the exact wait.
        let almost = last + CHECK_INTERVAL.as_millis() as u64 - secs(90);
        assert_eq!(next_step(&inputs(almost, Some(last))), Step::Sleep(Duration::from_secs(90)));
        let day = last + CHECK_INTERVAL.as_millis() as u64;
        assert_eq!(next_step(&inputs(day, Some(last))), Step::CheckNow);
        assert_eq!(next_step(&inputs(day + secs(5000), Some(last))), Step::CheckNow);
    }

    #[test]
    fn a_failed_check_waits_for_the_next_turn_like_any_other() {
        let mut after_error = inputs(START + secs(60), Some(START + secs(45)));
        after_error.phase = Phase::Error;
        assert_eq!(next_step(&after_error), Step::Sleep(MAX_SLEEP));
    }

    #[test]
    fn nothing_is_checked_while_the_setting_is_off() {
        let mut off = inputs(START + secs(500), None);
        off.enabled = false;
        assert_eq!(next_step(&off), Step::Sleep(MAX_SLEEP));
        off.last_attempt_ms = Some(START);
        off.now_ms += CHECK_INTERVAL.as_millis() as u64 * 3;
        assert_eq!(next_step(&off), Step::Sleep(MAX_SLEEP));
    }

    #[test]
    fn turning_the_setting_on_checks_at_once_if_it_is_due() {
        let mut on = inputs(START + secs(600), None);
        on.enabled = true;
        assert_eq!(next_step(&on), Step::CheckNow);
        // ...but not again if it just did.
        on.last_attempt_ms = Some(START + secs(599));
        assert_eq!(next_step(&on), Step::Sleep(MAX_SLEEP));
    }

    #[test]
    fn nothing_starts_over_an_install_or_a_check() {
        let due = START + secs(500);
        for (phase, wait) in [
            (Phase::Installing, MAX_SLEEP),
            (Phase::Available, MAX_SLEEP),
            (Phase::Checking, BUSY_WAIT),
            (Phase::Downloading, BUSY_WAIT),
        ] {
            let mut busy = inputs(due, None);
            busy.phase = phase;
            assert_eq!(next_step(&busy), Step::Sleep(wait), "{phase:?}");
        }
    }

    #[test]
    fn a_clock_set_back_checks_once_instead_of_waiting_for_it() {
        let last = START + secs(10_000);
        assert_eq!(next_step(&inputs(START + secs(100), Some(last))), Step::CheckNow);
    }

    #[test]
    fn a_check_always_leaves_a_wait_behind_it() {
        // The checker loops on this: right after a check, never CheckNow.
        let now = START + secs(500);
        assert_ne!(next_step(&inputs(now, Some(now))), Step::CheckNow);
    }
}
