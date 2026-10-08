//! What survives a restart: `%APPDATA%\Tack\board.json`, holding the pinned
//! prints in row order (each with its path, whether it is a capture or a
//! note, when it was pinned and when it was kept) and the settings.
//!
//! Only paths are stored, never images or text: on startup each file is
//! decoded (or a note's text read) again, and files that are gone by then are
//! skipped. A note's text lives only in its own file in Tack's Notes folder.
//!
//! The file carries a schema `version` ([`VERSION`]). Version 3 added notes
//! (a print's `"kind": "note"`); a version 2 file has only screenshots and
//! loads as is. Files without a version are the earlier layouts, still read:
//! the first `prints` layout, and before it bare `paths` (oldest first) and
//! `captures`, whose prints count as unkept and pinned when their file was
//! last modified. A print of a kind this build does not know is skipped. A file from a newer Tack
//! (a higher version) is read as far as it goes but never trusted for
//! cleanup, and is backed up before this build writes over it.
//!
//! Saving is atomic: the new contents go to `board.json.tmp`, are flushed to
//! disk, and only then replace board.json, so a crash mid-save leaves the old
//! file whole rather than a truncated one.

use std::collections::HashSet;
use std::ffi::OsString;
use std::io::Write;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use crate::files::path_key;
use crate::history;
use crate::print::{Kind, Origin, Print};
use crate::settings::Settings;

/// The board.json schema this build writes and fully understands. 3: notes.
pub const VERSION: u32 = 3;

/// The file's shape. Settings are flattened in, so the file reads
/// `{ "version": 3, "prints": [...], "sound": true, "edgeReveal": true, ... }`.
#[derive(Serialize, Deserialize, Default, Debug)]
#[serde(default, rename_all = "camelCase")]
struct Stored {
    /// Absent before versions were written; such files are layout 1 or older.
    version: Option<u32>,
    prints: Option<Vec<StoredPrint>>,
    /// The older layout: the pinned paths, oldest first. Read, never written.
    #[serde(skip_serializing)]
    paths: Vec<PathBuf>,
    /// The older layout: which of `paths` are captures.
    #[serde(skip_serializing)]
    captures: Vec<PathBuf>,
    #[serde(flatten)]
    settings: Settings,
}

/// One print in board.json.
#[derive(Serialize, Deserialize, Debug)]
#[serde(rename_all = "camelCase")]
struct StoredPrint {
    path: PathBuf,
    /// "note" for a note; absent (before version 3, and for screenshots)
    /// means a screenshot. Read as text, so a kind from a newer Tack skips
    /// that print instead of failing the whole file.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    kind: Option<String>,
    #[serde(default)]
    capture: bool,
    /// Ms since the Unix epoch.
    #[serde(default)]
    pinned_at: Option<u64>,
    #[serde(default)]
    kept: bool,
    /// Ms since the Unix epoch.
    #[serde(default)]
    kept_at: Option<u64>,
}

/// A print from board.json, to pin again.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SavedPrint {
    pub path: PathBuf,
    pub kind: Kind,
    pub origin: Origin,
    /// Ms since the Unix epoch.
    pub pinned_at: u64,
    /// Ms since the Unix epoch; `Some` for a kept print.
    pub kept_at: Option<u64>,
}

/// A board.json, read back.
#[derive(Debug, Default)]
pub struct Saved {
    pub settings: Settings,
    /// Last session's prints, in row order.
    pub prints: Vec<SavedPrint>,
}

/// How reading board.json went. Only [`Loaded::Ok`] means Tack knows for
/// certain which captures the board still uses, so only then may leftover
/// captures be swept.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Loaded {
    /// Read and understood.
    Ok,
    /// There is no board.json. Any captures lying around may belong to a
    /// board.json that was deleted, so they are left alone.
    Missing,
    /// board.json exists but could not be read, did not parse, or comes from
    /// a newer Tack. `backup` is where a copy of it was put, if one could be
    /// made; without one, nothing may write over the original.
    Unreadable { reason: String, backup: Option<PathBuf> },
}

impl Loaded {
    /// Whether leftover captures may be swept.
    pub fn may_sweep(&self) -> bool {
        matches!(self, Loaded::Ok)
    }

    /// Whether saving would overwrite a board.json that was never backed up.
    pub fn must_not_save(&self) -> bool {
        matches!(self, Loaded::Unreadable { backup: None, .. })
    }
}

impl Saved {
    /// Drops the prints that have aged out of the history by `now_ms` and
    /// hands them back, so they are never decoded just to be thrown away.
    pub fn age_out(&mut self, now_ms: u64) -> Vec<SavedPrint> {
        let entries: Vec<_> = self.prints.iter().map(|p| (p.kept_at, p.pinned_at)).collect();
        let gone = history::aged_out(&entries, now_ms);
        let (aged, stay): (Vec<_>, Vec<_>) =
            std::mem::take(&mut self.prints).into_iter().zip(gone).partition(|(_, gone)| *gone);
        self.prints = stay.into_iter().map(|(p, _)| p).collect();
        aged.into_iter().map(|(p, _)| p).collect()
    }
}

/// `%APPDATA%\Tack\board.json`.
pub fn board_file() -> PathBuf {
    let base = std::env::var_os("APPDATA").map(PathBuf::from).unwrap_or_else(|| PathBuf::from("."));
    base.join("Tack").join("board.json")
}

/// `file` with `suffix` added to its name: `board.json.tmp` and the like.
fn beside(file: &Path, suffix: &str) -> PathBuf {
    let mut name = OsString::from(file.as_os_str());
    name.push(suffix);
    PathBuf::from(name)
}

/// Where a save is written before it replaces board.json.
pub fn temp_file(file: &Path) -> PathBuf {
    beside(file, ".tmp")
}

/// Reads board.json and says how that went (see [`Loaded`]). Anything short
/// of [`Loaded::Ok`] still gives a usable board: the default settings and
/// whatever prints could be made out. A board.json that cannot be used as is
/// gets copied to `board.json.bad-<now_ms>` first, so no save can lose it.
///
/// Prints saved without a pin time get their file's modification time, or
/// `now_ms` if that cannot be read. A `.tmp` left by an interrupted save is
/// removed: it never replaced the real file, so it holds nothing needed.
pub fn load(file: &Path, now_ms: u64) -> (Saved, Loaded) {
    let _ = std::fs::remove_file(temp_file(file));
    let bytes = match std::fs::read(file) {
        Ok(bytes) => bytes,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return (Saved::default(), Loaded::Missing),
        Err(e) => {
            let reason = format!("cannot read it: {e}");
            return (Saved::default(), Loaded::Unreadable { reason, backup: back_up(file, now_ms) });
        }
    };
    let modified = |path: &Path| std::fs::metadata(path).and_then(|m| m.modified()).ok().and_then(history::epoch_ms);
    match parse(&bytes, now_ms, modified) {
        (saved, Loaded::Unreadable { reason, .. }) => {
            (saved, Loaded::Unreadable { reason, backup: back_up(file, now_ms) })
        }
        read => read,
    }
}

/// Copies a board.json this build cannot trust to `board.json.bad-<now_ms>`.
fn back_up(file: &Path, now_ms: u64) -> Option<PathBuf> {
    let backup = beside(file, &format!(".bad-{now_ms}"));
    match std::fs::copy(file, &backup) {
        Ok(_) => Some(backup),
        Err(e) => {
            eprintln!("tack: cannot back up {} to {}: {e}", file.display(), backup.display());
            None
        }
    }
}

/// Parses the contents of a board.json. `modified` gives a file's
/// modification time in ms since the Unix epoch, for prints saved without a
/// pin time; failing that they count as pinned at `now_ms`.
///
/// Contents that do not parse are an empty board. A newer schema is read as
/// far as this build understands it, but reported as
/// [`Loaded::Unreadable`]. The `backup` of an unreadable result is always
/// `None` here; [`load`] makes the copy.
pub fn parse(bytes: &[u8], now_ms: u64, modified: impl Fn(&Path) -> Option<u64>) -> (Saved, Loaded) {
    let stored: Stored = match serde_json::from_slice(bytes) {
        Ok(stored) => stored,
        Err(e) => {
            let reason = format!("it is not a board this build can read: {e}");
            return (Saved::default(), Loaded::Unreadable { reason, backup: None });
        }
    };
    let loaded = match stored.version {
        Some(v) if v > VERSION => Loaded::Unreadable {
            reason: format!("it was written by a newer Tack (schema {v}; this build knows up to {VERSION})"),
            backup: None,
        },
        _ => Loaded::Ok,
    };
    let entries = stored.prints.unwrap_or_else(|| legacy_prints(stored.paths, &stored.captures));
    let mut prints: Vec<SavedPrint> = entries
        .into_iter()
        .filter_map(|p| {
            let kind = match p.kind.as_deref() {
                None | Some("image") => Kind::Image,
                Some("note") => Kind::Note,
                Some(other) => {
                    eprintln!("tack: skipping a pinned {other:?}, a kind of print this build does not know");
                    return None;
                }
            };
            let pinned_at = p.pinned_at.or_else(|| modified(&p.path)).unwrap_or(now_ms);
            // A kept print saved without its time counts as kept when pinned.
            let kept_at = p.kept.then(|| p.kept_at.unwrap_or(pinned_at));
            // A note's file is always Tack's own.
            let origin = if p.capture || kind == Kind::Note { Origin::Capture } else { Origin::Folder };
            Some(SavedPrint { path: p.path, kind, origin, pinned_at, kept_at })
        })
        .collect();
    prints.sort_by_key(|p| history::row_key(p.kept_at, p.pinned_at));
    let mut settings = stored.settings;
    settings.retire_old_defaults();
    (Saved { settings, prints }, loaded)
}

/// The older layout as prints: unkept, with no pin time of their own.
fn legacy_prints(paths: Vec<PathBuf>, captures: &[PathBuf]) -> Vec<StoredPrint> {
    let captures: HashSet<String> = captures.iter().map(|p| path_key(p)).collect();
    // Oldest first there; newest first here, so ties keep the right order.
    paths
        .into_iter()
        .rev()
        .map(|path| StoredPrint {
            kind: None,
            capture: captures.contains(&path_key(&path)),
            path,
            pinned_at: None,
            kept: false,
            kept_at: None,
        })
        .collect()
}

/// Writes board.json, the prints in the order given (row order). Failures
/// are logged, never fatal: the board still works, it just will not remember.
pub fn save(file: &Path, prints: &[Print], settings: &Settings) {
    let stored = Stored {
        version: Some(VERSION),
        prints: Some(
            prints
                .iter()
                .map(|p| StoredPrint {
                    path: p.path.clone(),
                    kind: p.is_note().then(|| "note".to_string()),
                    capture: p.origin == Origin::Capture,
                    pinned_at: Some(p.pinned_at),
                    kept: p.kept,
                    kept_at: p.kept_at,
                })
                .collect(),
        ),
        settings: settings.clone(),
        ..Stored::default()
    };
    let written = serde_json::to_vec_pretty(&stored).map_err(std::io::Error::other).and_then(|b| replace(file, &b));
    if let Err(e) = written {
        eprintln!("tack: cannot save {}: {e}", file.display());
    }
}

/// Replaces `file` with `bytes` all at once: they are written and flushed to
/// a temporary file beside it, which is then renamed over it. On NTFS the
/// rename swaps the file in one step, so a reader (or the next start after a
/// crash) finds either the old contents or the new, never half of either.
fn replace(file: &Path, bytes: &[u8]) -> std::io::Result<()> {
    if let Some(dir) = file.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let temp = temp_file(file);
    let written = std::fs::File::create(&temp).and_then(|mut f| {
        f.write_all(bytes)?;
        f.sync_all()
    });
    let result = written.and_then(|()| std::fs::rename(&temp, file));
    if result.is_err() {
        let _ = std::fs::remove_file(&temp);
    }
    result
}

#[cfg(test)]
mod tests {
    use std::time::{Duration, SystemTime};

    use super::*;
    use crate::capture;
    use crate::history::{MAX_AGE_MS, MAX_UNKEPT};
    use crate::recycle::testing::NotingBin;

    const NOW: u64 = 1_800_000_000_000;

    fn no_files(_: &Path) -> Option<u64> {
        None
    }

    fn saved(path: &str, origin: Origin, pinned_at: u64, kept_at: Option<u64>) -> SavedPrint {
        SavedPrint { path: PathBuf::from(path), kind: Kind::Image, origin, pinned_at, kept_at }
    }

    /// A fresh, empty folder for one test.
    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("tack-store-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn print(dir: &Path, id: &str, name: &str, origin: Origin, pinned_at: u64, kept_at: Option<u64>) -> Print {
        let note = name.ends_with(".txt").then(|| crate::note::body(&format!("the words of {name}")).unwrap());
        Print {
            id: id.into(),
            name: name.into(),
            kind: if note.is_some() { Kind::Note } else { Kind::Image },
            thumb: String::new(),
            width: 1,
            height: 1,
            pinned_at,
            kept: kept_at.is_some(),
            kept_at,
            note,
            path: dir.join(name),
            stamp: None,
            origin,
            arrived: None,
        }
    }

    #[test]
    fn a_board_json_from_before_the_history_loads() {
        let old = br#"{
            "paths": ["C:\\Pics\\one.png", "C:\\Pics\\two.png", "C:\\Cap\\Three.png"],
            "captures": ["c:\\cap\\three.PNG"],
            "sound": false,
            "edgeReveal": true
        }"#;
        // Pin times come from the files, or now when a file has none.
        let modified = |p: &Path| match p.to_str() {
            Some(r"C:\Pics\one.png") => Some(NOW - 2000),
            Some(r"C:\Pics\two.png") => Some(NOW - 1000),
            _ => None,
        };
        let (saved_board, loaded) = parse(old, NOW, modified);
        assert_eq!(loaded, Loaded::Ok);
        assert_eq!(saved_board.settings, Settings { sound: false, edge_reveal: true, ..Settings::default() });
        assert_eq!(
            saved_board.prints,
            vec![
                // Captures were matched ignoring case; newest first.
                saved(r"C:\Cap\Three.png", Origin::Capture, NOW, None),
                saved(r"C:\Pics\two.png", Origin::Folder, NOW - 1000, None),
                saved(r"C:\Pics\one.png", Origin::Folder, NOW - 2000, None),
            ]
        );
    }

    #[test]
    fn missing_fields_take_their_defaults() {
        let json = br#"{ "prints": [
            { "path": "C:\\Pics\\a.png" },
            { "path": "C:\\Pics\\b.png", "kept": true, "pinnedAt": 5 },
            { "path": "C:\\Pics\\c.png", "kept": false, "keptAt": 9, "pinnedAt": 7 }
        ] }"#;
        let (saved_board, loaded) = parse(json, NOW, no_files);
        assert_eq!(loaded, Loaded::Ok, "a file without a version is the first prints layout");
        assert_eq!(saved_board.settings, Settings::default());
        assert_eq!(
            saved_board.prints,
            vec![
                // Kept without a time: kept when pinned.
                saved(r"C:\Pics\b.png", Origin::Folder, 5, Some(5)),
                saved(r"C:\Pics\a.png", Origin::Folder, NOW, None),
                // Not kept, whatever keptAt says.
                saved(r"C:\Pics\c.png", Origin::Folder, 7, None),
            ]
        );
    }

    #[test]
    fn known_versions_are_understood() {
        for v in 1..=VERSION {
            let json = format!(r#"{{ "version": {v}, "prints": [] }}"#);
            assert_eq!(parse(json.as_bytes(), NOW, no_files).1, Loaded::Ok);
        }
    }

    #[test]
    fn a_version_2_board_migrates_to_screenshots_and_default_shortcuts() {
        let v2 = br#"{ "version": 2, "prints": [
            { "path": "C:\\Cap\\a.png", "capture": true, "pinnedAt": 9, "kept": false },
            { "path": "C:\\Pics\\b.png", "pinnedAt": 8 }
        ], "sound": true, "edgeReveal": false, "snipTipShown": true }"#;
        let (saved_board, loaded) = parse(v2, NOW, no_files);
        assert_eq!(loaded, Loaded::Ok);
        assert!(saved_board.prints.iter().all(|p| p.kind == Kind::Image));
        assert_eq!(saved_board.prints[0], saved(r"C:\Cap\a.png", Origin::Capture, 9, None));
        // Ctrl+Alt+T is gone: a board from before shortcuts gets the new ones.
        assert_eq!(saved_board.settings.toggle_shortcut, "Win+Alt+S");
        assert_eq!(saved_board.settings.pin_shortcut, "Win+Alt+C");
        assert!(!saved_board.settings.edge_reveal);
    }

    #[test]
    fn notes_load_as_tacks_own_files_and_unknown_kinds_are_skipped() {
        let v3 = br#"{ "version": 3, "prints": [
            { "path": "C:\\Notes\\Note 1.txt", "kind": "note", "pinnedAt": 9 },
            { "path": "C:\\Pics\\b.png", "kind": "image", "pinnedAt": 8 },
            { "path": "C:\\Else\\c.ink", "kind": "sketch", "pinnedAt": 7 }
        ], "togglePinShortcut": "ignored", "pinShortcut": "" }"#;
        let (saved_board, loaded) = parse(v3, NOW, no_files);
        assert_eq!(loaded, Loaded::Ok);
        assert_eq!(saved_board.prints.len(), 2);
        let note = &saved_board.prints[0];
        assert_eq!((note.kind, note.origin), (Kind::Note, Origin::Capture), "a note's file is always Tack's own");
        assert_eq!(saved_board.prints[1].kind, Kind::Image);
        assert_eq!(saved_board.settings.pin_shortcut, "", "a shortcut turned off stays off");
    }

    #[test]
    fn only_notes_are_saved_with_a_kind() {
        let dir = scratch("kinds");
        let file = dir.join("board.json");
        let prints = [
            print(&dir, "1", "a.png", Origin::Folder, NOW - 1, None),
            print(&dir, "2", "Note.txt", Origin::Capture, NOW - 2, None),
        ];
        save(&file, &prints, &Settings::default());
        let text = std::fs::read_to_string(&file).unwrap();
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(text.matches("\"kind\"").count(), 1);
        assert!(text.contains("\"kind\": \"note\""));
        assert!(!text.contains("the words of"), "the note's text is never in board.json");
        assert!(text.contains("\"toggleShortcut\": \"Win+Alt+S\""));
    }

    #[test]
    fn damaged_contents_are_an_empty_board_that_cannot_be_trusted() {
        // Garbage, an empty file, and a save cut short.
        for bytes in [&b"not json"[..], b"", br#"{ "version": 2, "prints": [ { "path": "C:\\a"#] {
            let (saved_board, loaded) = parse(bytes, NOW, no_files);
            assert!(saved_board.prints.is_empty());
            assert_eq!(saved_board.settings, Settings::default());
            assert!(matches!(loaded, Loaded::Unreadable { .. }));
            assert!(!loaded.may_sweep());
        }
    }

    #[test]
    fn a_newer_schema_is_read_but_not_trusted() {
        let json = br#"{ "version": 99, "prints": [ { "path": "C:\\Cap\\a.png", "capture": true, "pinnedAt": 5 } ],
            "somethingNew": [1, 2, 3] }"#;
        let (saved_board, loaded) = parse(json, NOW, no_files);
        assert_eq!(saved_board.prints, vec![saved(r"C:\Cap\a.png", Origin::Capture, 5, None)]);
        assert!(matches!(loaded, Loaded::Unreadable { .. }));
        assert!(!loaded.may_sweep());
    }

    #[test]
    fn saved_prints_past_the_history_are_dropped() {
        let mut saved_board = Saved::default();
        saved_board.prints.push(saved("kept", Origin::Folder, NOW - 9 * MAX_AGE_MS, Some(NOW - MAX_AGE_MS)));
        saved_board.prints.push(saved("old", Origin::Capture, NOW - MAX_AGE_MS, None));
        for n in 0..MAX_UNKEPT as u64 + 1 {
            saved_board.prints.push(saved(&n.to_string(), Origin::Folder, NOW - 10 - n, None));
        }
        let aged = saved_board.age_out(NOW);
        let gone: Vec<_> = aged.iter().map(|p| p.path.to_str().unwrap()).collect();
        assert_eq!(gone, ["old", "50"]);
        assert_eq!(saved_board.prints.len(), MAX_UNKEPT + 1);
        assert_eq!(saved_board.prints[0].path, PathBuf::from("kept"));
    }

    #[test]
    fn save_then_load_round_trips_without_leaving_a_temp_file() {
        let dir = scratch("round-trip");
        let file = dir.join("board.json");
        let prints = [
            print(&dir, "1", "a.png", Origin::Folder, NOW - 50, Some(NOW - 10)),
            print(&dir, "2", "b.png", Origin::Capture, NOW - 20, None),
            print(&dir, "3", "c.txt", Origin::Capture, NOW - 30, None),
        ];
        let settings = Settings {
            sound: false,
            edge_reveal: false,
            snip_tip_shown: true,
            toggle_shortcut: "Ctrl+Alt+F9".into(),
            pin_shortcut: String::new(),
            phone: crate::phone::PhoneSettings {
                on: true,
                devices: vec![crate::phone::Device { key: "cGl4ZWw=".into(), name: "pixel".into(), approved_at: NOW }],
            },
        };
        save(&file, &prints, &settings);
        // Saving again replaces the file rather than failing on it.
        save(&file, &prints, &settings);
        let temp_left = temp_file(&file).exists();
        let text = std::fs::read_to_string(&file).unwrap();
        let (saved_board, loaded) = load(&file, NOW);
        let _ = std::fs::remove_dir_all(&dir);

        assert!(!temp_left, "the temporary file is renamed into place");
        assert!(text.contains(&format!("\"version\": {VERSION}")));
        assert_eq!(loaded, Loaded::Ok);
        assert_eq!(saved_board.settings, settings);
        assert_eq!(
            saved_board.prints,
            vec![
                SavedPrint {
                    path: dir.join("a.png"),
                    kind: Kind::Image,
                    origin: Origin::Folder,
                    pinned_at: NOW - 50,
                    kept_at: Some(NOW - 10)
                },
                SavedPrint {
                    path: dir.join("b.png"),
                    kind: Kind::Image,
                    origin: Origin::Capture,
                    pinned_at: NOW - 20,
                    kept_at: None
                },
                SavedPrint {
                    path: dir.join("c.txt"),
                    kind: Kind::Note,
                    origin: Origin::Capture,
                    pinned_at: NOW - 30,
                    kept_at: None
                },
            ]
        );
    }

    #[test]
    fn a_stale_temp_file_is_cleared_on_load() {
        let dir = scratch("stale-temp");
        let file = dir.join("board.json");
        save(&file, &[], &Settings::default());
        std::fs::write(temp_file(&file), b"{ \"prints\": [").unwrap();
        let (_, loaded) = load(&file, NOW);
        let temp_left = temp_file(&file).exists();
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(loaded, Loaded::Ok);
        assert!(!temp_left);
    }

    #[test]
    fn no_board_json_is_missing() {
        let dir = scratch("missing");
        let (saved_board, loaded) = load(&dir.join("board.json"), NOW);
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(loaded, Loaded::Missing);
        assert!(saved_board.prints.is_empty());
        assert!(!loaded.may_sweep());
        assert!(!loaded.must_not_save());
    }

    #[test]
    fn an_unusable_board_json_is_backed_up_before_any_save() {
        for (name, contents) in [
            ("corrupt", &br#"{ "prints": [ { "path": "#[..]),
            ("newer", br#"{ "version": 4, "prints": [], "layout": "whatever comes next" }"#),
        ] {
            let dir = scratch(name);
            let file = dir.join("board.json");
            std::fs::write(&file, contents).unwrap();
            let (_, loaded) = load(&file, NOW);
            // The app may now save over it; the original must survive.
            save(&file, &[], &Settings::default());
            let backup = beside(&file, &format!(".bad-{NOW}"));
            let backed_up = std::fs::read(&backup).ok();
            let (_, reloaded) = load(&file, NOW);
            let _ = std::fs::remove_dir_all(&dir);

            assert!(matches!(&loaded, Loaded::Unreadable { backup: Some(b), .. } if *b == backup), "{name}");
            assert!(!loaded.must_not_save());
            assert_eq!(backed_up.as_deref(), Some(contents), "{name}: the backup holds the original bytes");
            assert_eq!(reloaded, Loaded::Ok, "{name}: this build's own save reads back");
        }
    }

    /// The startup rule end to end: only a board.json that was understood
    /// lets the sweep run, and then only for old, unreferenced captures.
    #[test]
    fn only_an_understood_board_json_lets_the_sweep_run() {
        let later = SystemTime::now() + Duration::from_secs(60 * 60);
        let cases: [(&str, Option<String>); 4] = [
            ("missing", None),
            ("corrupt", Some("{ \"prints\": [".into())),
            ("newer", Some(r#"{ "version": 7, "prints": [] }"#.into())),
            ("understood", Some(format!(r#"{{ "version": {VERSION}, "prints": [] }}"#))),
        ];
        for (name, contents) in cases {
            let dir = scratch(&format!("sweep-{name}"));
            let captures = dir.join("Captures");
            std::fs::create_dir_all(&captures).unwrap();
            let leftover = captures.join("Screenshot 2026-10-08 023223.png");
            std::fs::write(&leftover, b"x").unwrap();
            let file = dir.join("board.json");
            if let Some(contents) = &contents {
                std::fs::write(&file, contents).unwrap();
            }

            let (saved_board, loaded) = load(&file, NOW);
            let pinned: Vec<PathBuf> = saved_board.prints.iter().map(|p| p.path.clone()).collect();
            let bin = NotingBin::default();
            if loaded.may_sweep() {
                capture::sweep(&captures, &pinned, later, &bin);
            }
            let taken = bin.taken.borrow().clone();
            let _ = std::fs::remove_dir_all(&dir);

            if name == "understood" {
                assert_eq!(taken, vec![leftover], "{name}");
            } else {
                assert!(taken.is_empty(), "{name}: nothing is swept");
            }
        }
    }
}
