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
