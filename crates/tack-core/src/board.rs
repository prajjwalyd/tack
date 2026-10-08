//! The board model: which prints (screenshots and notes) are pinned, in
//! what order, which of them are kept, and which have aged out of the
//! history.
//!
//! The prints always hang in row order (see [`crate::history`]): kept prints
//! first, then the rest newest first. A new print joins at the front of the
//! unkept ones. Nothing is ever pushed off for lack of room, since the row
//! scrolls; unkept prints leave only by ageing out.
//!
//! Every change returns what happened instead of acting on it, so the app
//! decides what to save, which events to send and which files to clean up.
//! Times come in as arguments (wall-clock ms for the history, an `Instant`
//! for spotting twins), which keeps all of it testable.

use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use crate::files::{file_name, file_stamp, path_key};
use crate::history;
use crate::print::{Kind, NoteBody, Origin, Print};
use crate::thumbnail::Thumb;

/// With auto-save on, Snipping Tool both saves the file and copies the image;
/// arrivals of the same size this close together are one screenshot.
pub const TWIN_WINDOW: Duration = Duration::from_secs(5);

/// How a print comes to the board.
#[derive(Clone, Copy, Debug)]
pub enum Arrival {
    /// A screenshot that has just been taken, reaching the board at this
    /// instant. It is pinned now, and may be the other half of an auto-saved
    /// screenshot pinned moments ago.
    Live(Instant),
    /// A print from last session, put back at startup as it was.
    Restored { pinned_at: u64, kept_at: Option<u64> },
}

/// How a pin went.
#[derive(Debug)]
pub enum PinOutcome {
    /// A new print, plus any unkept prints that aged out of the history
    /// meanwhile (the fifty-first pushes out the oldest).
    New { print: Print, aged: Vec<Print> },
    /// A restored print already past the history's limits: it was not
    /// pinned, and its capture file (if it is one) has no use any more.
    /// `aged` as for [`PinOutcome::New`].
    Expired { print: Print, aged: Vec<Print> },
    /// The screenshot file of a capture pinned moments ago: that print now
    /// shows the file, and the capture's own copy at `capture` is redundant.
    Merged { print: Print, capture: PathBuf },
    /// A capture of a screenshot file pinned moments ago; nothing was pinned.
    Duplicate,
    /// Already on the board: the same file, or a note with the same text.
    Exists,
}

/// What keeping or no longer keeping a print did.
#[derive(Debug)]
pub struct KeepChange {
    /// The print, as it is now.
    pub print: Print,
    /// The row's order changed.
    pub moved: bool,
    /// Prints that aged out because of it: a print that is no longer kept
    /// rejoins the history, and may be past its limits.
    pub aged: Vec<Print>,
}

#[derive(Debug)]
pub struct Board {
    /// In row order.
    prints: Vec<Print>,
    next_id: u64,
}

impl Default for Board {
    fn default() -> Self {
        Board { prints: Vec::new(), next_id: 1 }
    }
}

impl Board {
    pub fn new() -> Board {
        Board::default()
    }

    /// In row order: kept prints first, then the newest.
    pub fn prints(&self) -> &[Print] {
        &self.prints
    }

    /// The ids in row order.
    pub fn order(&self) -> Vec<String> {
        self.prints.iter().map(|p| p.id.clone()).collect()
    }

    pub fn find(&self, id: &str) -> Option<&Print> {
        self.prints.iter().find(|p| p.id == id)
    }

    pub fn find_path(&self, path: &Path) -> Option<&Print> {
        let key = path_key(path);
        self.prints.iter().find(|p| path_key(&p.path) == key)
    }

    /// A note already on the board with exactly this text.
    pub fn find_note(&self, text: &str) -> Option<&Print> {
        self.prints.iter().find(|p| p.note.as_ref().is_some_and(|n| n.text == text))
    }

    /// The other half of an auto-saved screenshot: a print of the other
    /// origin, the same size, that arrived moments ago. Notes have no twins.
    fn twin(&self, origin: Origin, width: u32, height: u32, now: Instant) -> Option<usize> {
        self.prints.iter().position(|p| {
            p.kind == Kind::Image
                && p.origin != origin
                && p.width == width
                && p.height == height
                && p.arrived.is_some_and(|t| now.saturating_duration_since(t) < TWIN_WINDOW)
        })
    }

    /// Pins a screenshot at `now_ms` (wall-clock ms since the Unix epoch).
    pub fn pin(&mut self, path: PathBuf, thumb: Thumb, origin: Origin, arrival: Arrival, now_ms: u64) -> PinOutcome {
        if self.find_path(&path).is_some() {
            return PinOutcome::Exists;
        }
        if let Arrival::Live(now) = arrival {
            if let Some(index) = self.twin(origin, thumb.width, thumb.height, now) {
                if origin == Origin::Capture {
                    return PinOutcome::Duplicate;
                }
                // Keep the user's own file rather than Tack's copy of it.
                let print = &mut self.prints[index];
                let capture = std::mem::replace(&mut print.path, path);
                print.origin = Origin::Folder;
                print.name = file_name(&print.path);
                print.thumb = thumb.data_url;
                print.stamp = file_stamp(&print.path);
                return PinOutcome::Merged { print: print.clone(), capture };
            }
        }

        let print = self.new_print(path, arrival, now_ms, |print| {
            print.thumb = thumb.data_url;
            print.width = thumb.width;
            print.height = thumb.height;
            print.origin = origin;
        });
        self.insert(print, arrival, now_ms)
    }

    /// Pins a note whose text is saved at `path` (one of Tack's own files).
    /// A live note with the same text as one already on the board is not
    /// pinned again ([`PinOutcome::Exists`]).
    pub fn pin_note(&mut self, path: PathBuf, body: NoteBody, arrival: Arrival, now_ms: u64) -> PinOutcome {
        if self.find_path(&path).is_some() {
            return PinOutcome::Exists;
        }
        if matches!(arrival, Arrival::Live(_)) && self.find_note(&body.text).is_some() {
            return PinOutcome::Exists;
        }
        let print = self.new_print(path, arrival, now_ms, |print| {
            print.kind = Kind::Note;
            print.origin = Origin::Capture;
            print.note = Some(body);
        });
        self.insert(print, arrival, now_ms)
    }

    /// A fresh print for `path`, with a new id and the times `arrival`
    /// gives (a live one is pinned at `now_ms`); `fill` sets what it shows.
    fn new_print(&mut self, path: PathBuf, arrival: Arrival, now_ms: u64, fill: impl FnOnce(&mut Print)) -> Print {
        let (pinned_at, kept_at, arrived) = match arrival {
            Arrival::Live(now) => (now_ms, None, Some(now)),
            Arrival::Restored { pinned_at, kept_at } => (pinned_at, kept_at, None),
        };
        let id = self.next_id.to_string();
        self.next_id += 1;
        let mut print = Print {
            id,
            name: file_name(&path),
            kind: Kind::Image,
            thumb: String::new(),
            width: 0,
            height: 0,
            pinned_at,
            kept: kept_at.is_some(),
            kept_at,
            note: None,
            stamp: file_stamp(&path),
            path,
            origin: Origin::Folder,
            arrived,
        };
        fill(&mut print);
        print
    }

    /// Puts a new print in its place in the row and ages out what that
    /// pushes past the history's limits.
    fn insert(&mut self, print: Print, arrival: Arrival, now_ms: u64) -> PinOutcome {
        // A live print goes ahead of any print pinned at the same moment; a
        // restored one comes back in the order it was saved.
        match arrival {
            Arrival::Live(_) => self.prints.insert(0, print.clone()),
            Arrival::Restored { .. } => self.prints.push(print.clone()),
        }
        self.arrange();
        let mut aged = self.age_out(now_ms);
        match aged.iter().position(|p| p.id == print.id) {
            Some(index) => PinOutcome::Expired { print: aged.remove(index), aged },
            None => PinOutcome::New { print, aged },
        }
    }

    /// Takes a print off the board and hands it back.
    pub fn remove(&mut self, id: &str) -> Option<Print> {
        let index = self.prints.iter().position(|p| p.id == id)?;
        Some(self.prints.remove(index))
    }

    /// Keeps a print (it moves to the end of the kept ones) or stops keeping
    /// it (it goes back into the history by when it was pinned, and ages out
    /// at once if it is past the history's limits). Keeping a kept print
    /// changes nothing, so it keeps its place.
    pub fn set_kept(&mut self, id: &str, kept: bool, now_ms: u64) -> Option<KeepChange> {
        let before = self.order();
        let print = self.prints.iter_mut().find(|p| p.id == id)?;
        if print.kept != kept {
            print.kept = kept;
            print.kept_at = kept.then_some(now_ms);
        }
        let print = print.clone();
        self.arrange();
        let moved = self.order() != before;
        let aged = if kept { Vec::new() } else { self.age_out(now_ms) };
        Some(KeepChange { print, moved, aged })
    }

    /// Takes off every unkept print past the history's limits at `now_ms`
    /// (see [`history::aged_out`]) and hands them back, in row order.
    pub fn age_out(&mut self, now_ms: u64) -> Vec<Print> {
        let entries: Vec<_> = self.prints.iter().map(|p| (p.kept_at, p.pinned_at)).collect();
        let gone = history::aged_out(&entries, now_ms);
        if !gone.contains(&true) {
            return Vec::new();
        }
        let (aged, stay): (Vec<_>, Vec<_>) =
            std::mem::take(&mut self.prints).into_iter().zip(gone).partition(|(_, gone)| *gone);
        self.prints = stay.into_iter().map(|(p, _)| p).collect();
        aged.into_iter().map(|(p, _)| p).collect()
    }

    /// Puts the prints in row order. Stable, so ties keep their places.
    fn arrange(&mut self) {
        self.prints.sort_by_key(|p| history::row_key(p.kept_at, p.pinned_at));
    }

    /// Points a print at its file's new place. Returns the updated print and
    /// the old path, so a move that fails can be undone.
    pub fn repoint(&mut self, id: &str, path: PathBuf, origin: Origin) -> Option<(Print, PathBuf)> {
        let print = self.prints.iter_mut().find(|p| p.id == id)?;
        let old = std::mem::replace(&mut print.path, path);
        print.origin = origin;
        print.name = file_name(&print.path);
        Some((print.clone(), old))
    }

    /// Notes the file's current modification time and size, once Tack has
    /// finished writing a capture's file: from then on only an edit counts
    /// as a change. False if no print shows that file (any more).
    pub fn refresh_stamp(&mut self, path: &Path) -> bool {
        let key = path_key(path);
        let Some(print) = self.prints.iter_mut().find(|p| path_key(&p.path) == key) else { return false };
        print.stamp = file_stamp(path);
        true
    }

    /// Swaps in a note's text after its file was edited on disk. `None` if
    /// no note shows that file.
    pub fn update_note(&mut self, path: &Path, body: NoteBody) -> Option<Print> {
        let key = path_key(path);
        let print = self.prints.iter_mut().find(|p| p.is_note() && path_key(&p.path) == key)?;
        print.note = Some(body);
        print.stamp = file_stamp(path);
        Some(print.clone())
    }

    /// Swaps in a fresh thumbnail after the file was edited on disk.
    pub fn update_thumb(&mut self, path: &Path, thumb: Thumb) -> Option<Print> {
        let key = path_key(path);
        let print = self.prints.iter_mut().find(|p| path_key(&p.path) == key)?;
        print.thumb = thumb.data_url;
        print.width = thumb.width;
        print.height = thumb.height;
        print.stamp = file_stamp(path);
        Some(print.clone())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::capture;
    use crate::history::{MAX_AGE_MS, MAX_UNKEPT};
    use crate::recycle::testing::NotingBin;

    /// A fixed "now" on the wall clock, and one minute in ms.
    const NOW: u64 = 1_800_000_000_000;
    const MIN: u64 = 60_000;

    fn thumb(width: u32, height: u32) -> Thumb {
        Thumb { data_url: format!("data:image/jpeg;base64,{width}x{height}"), width, height }
    }

    fn path(name: &str) -> PathBuf {
        PathBuf::from(format!(r"C:\nowhere\{name}.png"))
    }

    /// A live screenshot of the usual size, `n` minutes after NOW.
    fn live(board: &mut Board, name: &str, origin: Origin, n: u64) -> PinOutcome {
        let at = Instant::now() + Duration::from_secs(60 * n);
        board.pin(path(name), thumb(1920, 1080), origin, Arrival::Live(at), NOW + n * MIN)
    }

    /// Pins `count` live screenshot files of different sizes (never twins),
    /// a minute apart, starting at minute `from`.
    fn pin_many(board: &mut Board, from: u64, count: u64) {
        for n in from..from + count {
            let at = Instant::now();
            board.pin(path(&n.to_string()), thumb(n as u32 + 1, 1), Origin::Folder, Arrival::Live(at), NOW + n * MIN);
        }
    }

    fn restored(board: &mut Board, name: &str, pinned_at: u64, kept_at: Option<u64>) -> PinOutcome {
        board.pin(path(name), thumb(800, 600), Origin::Folder, Arrival::Restored { pinned_at, kept_at }, NOW)
    }

    fn ids(board: &Board) -> Vec<&str> {
        board.prints().iter().map(|p| p.id.as_str()).collect()
    }

    fn id_list(prints: &[Print]) -> Vec<&str> {
        prints.iter().map(|p| p.id.as_str()).collect()
    }

    #[test]
    fn new_prints_join_at_the_front_newest_first() {
        let mut board = Board::new();
        pin_many(&mut board, 0, 3);
        assert_eq!(ids(&board), ["3", "2", "1"]);
        assert_eq!(board.order(), ["3", "2", "1"]);
        assert_eq!(board.find("3").unwrap().pinned_at, NOW + 2 * MIN);
        assert!(!board.find("3").unwrap().kept);
    }

    #[test]
    fn kept_prints_lead_in_the_order_they_were_kept() {
        let mut board = Board::new();
        pin_many(&mut board, 0, 4);
        let first = board.set_kept("2", true, NOW + 10 * MIN).unwrap();
        assert!(first.moved);
        assert!(first.print.kept);
        assert_eq!(first.print.kept_at, Some(NOW + 10 * MIN));
        board.set_kept("4", true, NOW + 11 * MIN).unwrap();
        assert_eq!(ids(&board), ["2", "4", "3", "1"]);
        // A newcomer joins after the kept prints, at the front of the rest.
        pin_many(&mut board, 20, 1);
        assert_eq!(ids(&board), ["2", "4", "5", "3", "1"]);
    }

    #[test]
    fn keeping_twice_changes_nothing() {
        let mut board = Board::new();
        pin_many(&mut board, 0, 3);
        board.set_kept("1", true, NOW + 10 * MIN);
        board.set_kept("2", true, NOW + 11 * MIN);
        let again = board.set_kept("1", true, NOW + 12 * MIN).unwrap();
        assert!(!again.moved);
        assert_eq!(again.print.kept_at, Some(NOW + 10 * MIN));
        assert_eq!(ids(&board), ["1", "2", "3"]);
        assert!(board.set_kept("nope", true, NOW).is_none());
    }

    #[test]
    fn a_print_no_longer_kept_goes_back_by_when_it_was_pinned() {
        let mut board = Board::new();
        pin_many(&mut board, 0, 4);
        board.set_kept("2", true, NOW + 10 * MIN);
        assert_eq!(ids(&board), ["2", "4", "3", "1"]);
        let change = board.set_kept("2", false, NOW + 11 * MIN).unwrap();
        assert!(change.moved);
        assert!(!change.print.kept);
        assert_eq!(change.print.kept_at, None);
        assert!(change.aged.is_empty());
        assert_eq!(ids(&board), ["4", "3", "2", "1"]);
    }

    #[test]
    fn a_print_no_longer_kept_ages_out_if_it_is_past_the_history() {
        let mut board = Board::new();
        pin_many(&mut board, 0, 2);
        board.set_kept("1", true, NOW + 5 * MIN);
        // Eight days later the unkept one has aged out; the kept one stays.
        let later = NOW + MAX_AGE_MS + 24 * 60 * MIN;
        assert_eq!(id_list(&board.age_out(later)), ["2"]);
        assert_eq!(ids(&board), ["1"]);
        let change = board.set_kept("1", false, later).unwrap();
        assert_eq!(id_list(&change.aged), ["1"]);
        assert!(board.prints().is_empty());
    }

    #[test]
    fn the_fifty_first_unkept_print_ages_out_the_oldest() {
        let mut board = Board::new();
        pin_many(&mut board, 0, MAX_UNKEPT as u64);
        // Kept prints do not count against the history.
        board.set_kept("1", true, NOW + 100 * MIN);
        board.set_kept("2", true, NOW + 101 * MIN);
        pin_many(&mut board, 200, 2);
        assert_eq!(board.prints().len(), MAX_UNKEPT + 2);
        let PinOutcome::New { print, aged } = live(&mut board, "fresh", Origin::Folder, 300) else {
            panic!("expected a new print");
        };
        assert_eq!(print.id, "53");
        // The oldest unkept print is 3 (1 and 2 are kept).
        assert_eq!(id_list(&aged), ["3"]);
        assert_eq!(&ids(&board)[..3], ["1", "2", "53"]);
        assert_eq!(board.prints().iter().filter(|p| !p.kept).count(), MAX_UNKEPT);
    }

    #[test]
    fn unkept_prints_age_out_after_a_week_and_kept_ones_never() {
        let mut board = Board::new();
        pin_many(&mut board, 0, 3);
        board.set_kept("1", true, NOW + 10 * MIN);
        // A week after print 2 was pinned: 2 goes, 3 (a minute younger) stays.
        let week = NOW + MIN + MAX_AGE_MS;
        assert_eq!(id_list(&board.age_out(week)), ["2"]);
        assert_eq!(ids(&board), ["1", "3"]);
        assert!(board.age_out(week).is_empty());
        let years = NOW + 400 * MAX_AGE_MS;
        assert_eq!(id_list(&board.age_out(years)), ["3"]);
        assert_eq!(ids(&board), ["1"]);
    }

    #[test]
    fn restored_prints_come_back_in_place() {
        let mut board = Board::new();
        // Saved in row order: a kept print, then the history newest first.
        restored(&mut board, "kept", NOW - 30 * MAX_AGE_MS, Some(NOW - MAX_AGE_MS));
        restored(&mut board, "newer", NOW - 2 * MIN, None);
        restored(&mut board, "older", NOW - 5 * MIN, None);
        assert_eq!(ids(&board), ["1", "2", "3"]);
        let kept = board.find("1").unwrap();
        assert!(kept.kept);
        assert_eq!(kept.pinned_at, NOW - 30 * MAX_AGE_MS);
        assert!(kept.arrived.is_none());
    }

    #[test]
    fn a_restored_print_past_the_history_is_not_pinned() {
        let mut board = Board::new();
        let outcome = restored(&mut board, "old", NOW - MAX_AGE_MS - MIN, None);
        let PinOutcome::Expired { print, aged } = outcome else { panic!("expected it to have expired") };
        assert_eq!(print.path, path("old"));
        assert!(aged.is_empty());
        assert!(board.prints().is_empty());
    }

    #[test]
    fn ageing_out_recycles_capture_files_only() {
        let dir = std::env::temp_dir().join(format!("tack-ageing-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let screenshot = dir.join("screenshot.png");
        let capture_file = dir.join("capture.png");
        std::fs::write(&screenshot, b"x").unwrap();
        std::fs::write(&capture_file, b"x").unwrap();

        let mut board = Board::new();
        let at = Instant::now();
        board.pin(screenshot.clone(), thumb(10, 10), Origin::Folder, Arrival::Live(at), NOW);
        board.pin(capture_file.clone(), thumb(20, 20), Origin::Capture, Arrival::Live(at), NOW);
        let aged = board.age_out(NOW + MAX_AGE_MS);
        assert_eq!(aged.len(), 2);
        let bin = NotingBin::default();
        aged.iter().for_each(|p| capture::discard_file(p, &dir, &bin));
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(*bin.taken.borrow(), vec![capture_file], "only Tack's own capture goes to the bin");
    }

    #[test]
    fn a_screenshot_file_after_its_capture_replaces_it() {
        let mut board = Board::new();
        assert!(matches!(live(&mut board, "capture", Origin::Capture, 0), PinOutcome::New { .. }));
        let at = Instant::now() + Duration::from_secs(1);
        let outcome = board.pin(path("file"), thumb(1920, 1080), Origin::Folder, Arrival::Live(at), NOW + 1000);
        let PinOutcome::Merged { print, capture } = outcome else {
            panic!("expected a merge");
        };
        assert_eq!(capture, path("capture"));
        assert_eq!(print.id, "1");
        assert_eq!(print.path, path("file"));
        assert_eq!(print.name, "file.png");
        assert_eq!(print.origin, Origin::Folder);
        assert_eq!(print.pinned_at, NOW);
        assert_eq!(board.prints().len(), 1);
    }

    #[test]
    fn a_capture_after_its_screenshot_file_is_a_duplicate() {
        let mut board = Board::new();
        let at = Instant::now();
        board.pin(path("file"), thumb(1920, 1080), Origin::Folder, Arrival::Live(at), NOW);
        let later = Arrival::Live(at + Duration::from_secs(1));
        let outcome = board.pin(path("capture"), thumb(1920, 1080), Origin::Capture, later, NOW + 1000);
        assert!(matches!(outcome, PinOutcome::Duplicate));
        assert_eq!(ids(&board), ["1"]);
        assert_eq!(board.prints()[0].origin, Origin::Folder);
    }

    #[test]
    fn twins_must_be_recent_and_the_same_size() {
        let mut board = Board::new();
        let at = Instant::now();
        board.pin(path("capture"), thumb(1920, 1080), Origin::Capture, Arrival::Live(at), NOW);
        // Too late to be the same screenshot.
        let late = Arrival::Live(at + TWIN_WINDOW);
        let outcome = board.pin(path("file"), thumb(1920, 1080), Origin::Folder, late, NOW);
        assert!(matches!(outcome, PinOutcome::New { .. }));
        // Another size is another screenshot.
        let other = board.pin(path("small"), thumb(640, 480), Origin::Folder, Arrival::Live(at), NOW);
        assert!(matches!(other, PinOutcome::New { .. }));
        // Restored prints never merge.
        let again = Arrival::Restored { pinned_at: NOW, kept_at: None };
        let restored = board.pin(path("restored"), thumb(1920, 1080), Origin::Folder, again, NOW);
        assert!(matches!(restored, PinOutcome::New { .. }));
    }

    #[test]
    fn the_same_file_is_pinned_once() {
        let mut board = Board::new();
        live(&mut board, "a", Origin::Folder, 0);
        let upper = PathBuf::from(r"C:\NOWHERE\A.PNG");
        let outcome = board.pin(upper, thumb(1, 1), Origin::Folder, Arrival::Live(Instant::now()), NOW);
        assert!(matches!(outcome, PinOutcome::Exists));
    }

    #[test]
    fn repointing_keeps_the_print_and_returns_the_old_path() {
        let mut board = Board::new();
        live(&mut board, "capture", Origin::Capture, 0);
        let (print, old) = board.repoint("1", path("saved"), Origin::Folder).unwrap();
        assert_eq!(old, path("capture"));
        assert_eq!(print.name, "saved.png");
        assert_eq!(board.find("1").unwrap().origin, Origin::Folder);
        assert!(board.repoint("nope", path("x"), Origin::Folder).is_none());
    }

    fn note(text: &str) -> NoteBody {
        crate::note::body(text).unwrap()
    }

    fn note_path(name: &str) -> PathBuf {
        PathBuf::from(format!(r"C:\nowhere\Notes\{name}.txt"))
    }

    fn live_note(board: &mut Board, name: &str, text: &str, n: u64) -> PinOutcome {
        let at = Instant::now() + Duration::from_secs(60 * n);
        board.pin_note(note_path(name), note(text), Arrival::Live(at), NOW + n * MIN)
    }

    #[test]
    fn notes_join_the_same_row_as_prints() {
        let mut board = Board::new();
        live(&mut board, "shot", Origin::Folder, 0);
        let PinOutcome::New { print, aged } = live_note(&mut board, "note", "Call Sam back", 1) else {
            panic!("expected a new note");
        };
        assert!(aged.is_empty());
        assert_eq!(print.kind, Kind::Note);
        assert!(print.owned(), "Tack wrote the note's file, so it cleans it up");
        assert_eq!(print.note.as_ref().unwrap().text, "Call Sam back");
        assert_eq!((print.thumb.as_str(), print.width, print.height), ("", 0, 0));
        assert_eq!(print.name, "note.txt");
        assert_eq!(ids(&board), ["2", "1"]);
        // Kept like any print.
        board.set_kept("2", true, NOW + 5 * MIN);
        live(&mut board, "later", Origin::Folder, 6);
        assert_eq!(ids(&board), ["2", "3", "1"]);
    }

    #[test]
    fn the_same_text_is_pinned_once() {
        let mut board = Board::new();
        live_note(&mut board, "a", "https://example.org", 0);
        assert!(matches!(live_note(&mut board, "b", "https://example.org", 1), PinOutcome::Exists));
        assert!(matches!(live_note(&mut board, "c", "https://example.org/other", 2), PinOutcome::New { .. }));
        // A restored note never counts as a repeat: board.json said it was there.
        let restored = Arrival::Restored { pinned_at: NOW, kept_at: None };
        let again = board.pin_note(note_path("d"), note("https://example.org"), restored, NOW);
        assert!(matches!(again, PinOutcome::New { .. }));
        assert_eq!(board.find_note("https://example.org").unwrap().id, "1");
    }

    #[test]
    fn notes_age_out_like_prints() {
        let mut board = Board::new();
        live_note(&mut board, "old", "old news", 0);
        live_note(&mut board, "kept", "keep me", 1);
        board.set_kept("2", true, NOW + 2 * MIN);
        let aged = board.age_out(NOW + MAX_AGE_MS + MIN);
        assert_eq!(id_list(&aged), ["1"]);
        assert_eq!(ids(&board), ["2"]);
        // They count toward the history's fifty, too.
        pin_many(&mut board, 10, MAX_UNKEPT as u64);
        let PinOutcome::New { aged, .. } = live_note(&mut board, "new", "one more", 100) else {
            panic!("expected a new note");
        };
        assert_eq!(aged.len(), 1);
        assert_eq!(board.prints().iter().filter(|p| !p.kept).count(), MAX_UNKEPT);
    }

    #[test]
    fn a_note_never_merges_with_a_screenshot() {
        let mut board = Board::new();
        let at = Instant::now();
        board.pin_note(note_path("n"), note("text"), Arrival::Live(at), NOW);
        // A screenshot file "of the same size" (0 x 0) right after it.
        let outcome = board.pin(path("file"), thumb(0, 0), Origin::Folder, Arrival::Live(at), NOW);
        assert!(matches!(outcome, PinOutcome::New { .. }));
        assert_eq!(board.prints().len(), 2);
    }

    #[test]
    fn an_edited_note_file_updates_its_text() {
        let mut board = Board::new();
        live_note(&mut board, "n", "draft", 0);
        let print = board.update_note(&note_path("n"), note("final")).unwrap();
        assert_eq!(print.note.unwrap().text, "final");
        assert!(board.update_note(&path("n"), note("x")).is_none(), "only a note's own file");
    }
}
