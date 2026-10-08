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
