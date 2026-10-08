//! The rules that shape the row of prints: the order they hang in, and how
//! long the board remembers a screenshot nobody chose to keep.
//!
//! The row reads like a timeline running backwards. Kept prints come first,
//! in the order they were kept; after them the rest of the history, newest
//! first, so scrolling right goes back in time. That history is short-term
//! memory, not an archive: an unkept print leaves once it is a week old, or
//! once fifty newer unkept prints have arrived. Kept prints stay until they
//! are unpinned.
//!
//! Times are wall-clock milliseconds since the Unix epoch, passed in by the
//! caller, so every rule here can be tested with whatever "now" it likes.

use std::cmp::Reverse;
use std::time::{SystemTime, UNIX_EPOCH};

/// How long an unkept print stays on the board: seven days, in ms.
pub const MAX_AGE_MS: u64 = 7 * 24 * 60 * 60 * 1000;
/// How many unkept prints the history holds; one more ages out the oldest.
pub const MAX_UNKEPT: usize = 50;

/// The wall clock, in ms since the Unix epoch.
pub fn now_ms() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_millis() as u64).unwrap_or(0)
}

/// A file time as ms since the Unix epoch.
pub fn epoch_ms(time: SystemTime) -> Option<u64> {
    time.duration_since(UNIX_EPOCH).ok().map(|d| d.as_millis() as u64)
}

/// Where a print sorts in the row: kept prints first, by when they were
/// kept; then unkept prints, newest first. Sort with a stable sort, so prints
/// that tie keep their relative order.
pub fn row_key(kept_at: Option<u64>, pinned_at: u64) -> (bool, u64, Reverse<u64>) {
    match kept_at {
        Some(kept_at) => (false, kept_at, Reverse(0)),
        None => (true, 0, Reverse(pinned_at)),
    }
}

/// Which of `entries` have aged out at `now_ms`. Each entry is
/// `(kept_at, pinned_at)`; the answer is one flag per entry, in the same
/// order. An unkept entry ages out when it was pinned [`MAX_AGE_MS`] or more
/// ago, or when [`MAX_UNKEPT`] newer unkept entries are ahead of it. The
/// entries may come in any order; among unkept entries pinned at the same
/// moment, the earlier one in `entries` counts as the newer.
pub fn aged_out(entries: &[(Option<u64>, u64)], now_ms: u64) -> Vec<bool> {
    let mut unkept: Vec<usize> = (0..entries.len()).filter(|&i| entries[i].0.is_none()).collect();
    unkept.sort_by_key(|&i| Reverse(entries[i].1));
    let mut out = vec![false; entries.len()];
    for (rank, i) in unkept.into_iter().enumerate() {
        let age = now_ms.saturating_sub(entries[i].1);
        out[i] = rank >= MAX_UNKEPT || age >= MAX_AGE_MS;
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    const HOUR: u64 = 60 * 60 * 1000;
    const NOW: u64 = 1_800_000_000_000;

    #[test]
    fn kept_prints_lead_in_keeping_order_then_the_newest() {
        let mut row = [(None, 10), (Some(500), 1), (None, 30), (Some(200), 2), (None, 20)];
        row.sort_by_key(|&(kept_at, pinned_at)| row_key(kept_at, pinned_at));
        assert_eq!(row, [(Some(200), 2), (Some(500), 1), (None, 30), (None, 20), (None, 10)]);
    }

    #[test]
    fn a_week_old_unkept_print_ages_out() {
        let entries = [(None, NOW - MAX_AGE_MS + HOUR), (None, NOW - MAX_AGE_MS), (Some(NOW), NOW - 30 * MAX_AGE_MS)];
        // Just under a week stays; a week exactly goes; kept never goes.
        assert_eq!(aged_out(&entries, NOW), [false, true, false]);
    }

    #[test]
    fn the_history_holds_fifty_unkept_prints() {
        // Oldest first, as an old board.json lists them, with kept ones mixed in.
        let mut entries: Vec<(Option<u64>, u64)> = (0..MAX_UNKEPT as u64 + 2).map(|n| (None, NOW - 100 + n)).collect();
        entries.insert(0, (Some(NOW), NOW - 1000));
        entries.push((Some(NOW), NOW - 2000));
        let out = aged_out(&entries, NOW);
        // The two oldest unkept go; kept ones are not counted.
        assert_eq!(out.iter().filter(|&&gone| gone).count(), 2);
        assert!(out[1] && out[2]);
        assert!(!out[0] && !out[out.len() - 1]);
    }

    #[test]
    fn ties_count_the_earlier_entry_as_newer() {
        let entries: Vec<(Option<u64>, u64)> = vec![(None, NOW); MAX_UNKEPT + 1];
        let out = aged_out(&entries, NOW);
        assert!(out[..MAX_UNKEPT].iter().all(|&gone| !gone));
        assert!(out[MAX_UNKEPT]);
    }

    #[test]
    fn a_clock_that_went_back_ages_nothing() {
        assert_eq!(aged_out(&[(None, NOW + HOUR)], NOW), [false]);
    }
}
