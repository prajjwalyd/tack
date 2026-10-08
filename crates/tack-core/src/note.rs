//! Notes: short texts and links pinned to the board on purpose (Win+Alt+C on
//! a selection, or a drop on the board). Nothing reads text off the clipboard
//! on its own.
//!
//! Each note is a UTF-8 text file in `%LOCALAPPDATA%\Tack\Notes`, so it can be
//! dragged out, opened and edited like any file, and board.json holds only
//! its path. Like a capture, a note's file goes to the Recycle Bin when the
//! note leaves the board. A note whose whole text is one web link is a
//! **link**: the board shows its domain, and opening it opens the browser.

use std::path::{Path, PathBuf};
use std::time::SystemTime;

use crate::capture::{self, Timestamp};
use crate::print::NoteBody;
use crate::recycle::Recycler;

/// The most text a note holds, in bytes of UTF-8.
pub const MAX_NOTE_BYTES: usize = 20 * 1024;
/// A link longer than this is kept as plain text.
const MAX_LINK_LEN: usize = 4096;

/// Where notes are saved: `%LOCALAPPDATA%\Tack\Notes`.
pub fn notes_folder() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(|| PathBuf::from("."));
    base.join("Tack").join("Notes")
}

/// A note's file: a `.txt`.
fn is_note_file(path: &Path) -> bool {
    path.extension().and_then(|e| e.to_str()).is_some_and(|e| e.eq_ignore_ascii_case("txt"))
}

/// What a note made of `text` holds, or `None` if there is nothing to pin
/// (only whitespace). Line breaks become `\n`, blank lines around the text
/// go, and text past [`MAX_NOTE_BYTES`] is cut.
pub fn body(text: &str) -> Option<NoteBody> {
    let normal = normalise(text);
    if normal.is_empty() {
        return None;
    }
    let (text, truncated) = clamp(normal);
    let link = if truncated { None } else { link_of(&text) };
    let domain = link.as_deref().and_then(domain_of);
    Some(NoteBody { text, link, domain, truncated })
}

/// `\r\n` and `\r` become `\n`, other control characters go, and so do
/// blank lines before the text and all whitespace after it. The first line's
/// indentation stays (it may be code).
fn normalise(text: &str) -> String {
    let text = text.replace("\r\n", "\n").replace('\r', "\n");
    // A copied picture's placeholder character and NULs are not text.
    let text: String = text.chars().filter(|&c| !c.is_control() || c == '\n' || c == '\t').collect();
    let text = text.trim_end();
    let start = text.split_inclusive('\n').take_while(|line| line.trim().is_empty()).map(str::len).sum::<usize>();
    text[start..].to_string()
}

/// Cuts `text` to at most [`MAX_NOTE_BYTES`], at a character boundary.
/// Returns the text and whether anything was cut.
fn clamp(mut text: String) -> (String, bool) {
    if text.len() <= MAX_NOTE_BYTES {
        return (text, false);
    }
    let mut end = MAX_NOTE_BYTES;
    while !text.is_char_boundary(end) {
        end -= 1;
    }
    text.truncate(end);
    let kept = text.trim_end().len();
    text.truncate(kept);
    (text, true)
}

/// The URL, when the whole of `text` is one web link: `http://` or
/// `https://` (any case) followed by a host, with no whitespace, quotes or
/// control characters anywhere. A bare `www.` address counts too, as https.
/// Nothing else (no `file:`, no `mailto:`, no paths): only these are ever
/// handed to the browser.
pub fn link_of(text: &str) -> Option<String> {
    let text = text.trim();
    if text.is_empty() || text.len() > MAX_LINK_LEN {
        return None;
    }
    if text.chars().any(|c| c.is_whitespace() || c.is_control() || matches!(c, '"' | '<' | '>' | '`' | '\\')) {
        return None;
    }
    let lower = text.to_ascii_lowercase();
    let url = if lower.starts_with("https://") || lower.starts_with("http://") {
        text.to_string()
    } else if lower.starts_with("www.") {
        format!("https://{text}")
    } else {
        return None;
    };
    domain_of(&url)?;
    Some(url)
}

/// The host of an http(s) URL, lower case, without "www.", a port or a user
/// name: "https://www.GitHub.com:443/a?b" gives "github.com". `None` if
/// there is no plausible host.
fn domain_of(url: &str) -> Option<String> {
    let rest = url.split_once("://")?.1;
    let authority = rest.split(['/', '?', '#']).next()?;
    let host = authority.rsplit_once('@').map_or(authority, |(_, host)| host);
    let host = if host.starts_with('[') {
        // An IPv6 literal keeps its brackets and loses only the port.
        host.split_inclusive(']').next()?
    } else {
        host.split(':').next()?
    };
    let host = host.to_ascii_lowercase();
    let valid = !host.is_empty()
        && host.chars().all(|c| c.is_alphanumeric() || matches!(c, '.' | '-' | '_' | '[' | ']' | ':'))
        && !host.starts_with('.')
        && !host.ends_with('.');
    if !valid {
        return None;
    }
    Some(host.strip_prefix("www.").filter(|h| !h.is_empty()).unwrap_or(&host).to_string())
}

/// The note's file name stem, `Note YYYY-MM-DD HHMMSS`.
fn file_stem(at: Timestamp) -> String {
    format!("Note {}", at.stamp())
}

/// Saves a note's text as `Note YYYY-MM-DD HHMMSS.txt` in `dir` (adding
/// " (2)" and so on if that name is taken): UTF-8 with Windows line breaks,
/// written whole to a partial file first and then moved into place.
pub fn save(body: &NoteBody, dir: &Path, at: Timestamp) -> std::io::Result<PathBuf> {
    let path = capture::reserve_as(dir, &file_stem(at), "txt")?;
    capture::write_reserved_bytes(body.text.replace('\n', "\r\n").as_bytes(), &path)?;
    Ok(path)
}

/// Reads a note's file back: a byte order mark goes, invalid UTF-8 is
/// replaced, and the text is normalised and clamped as when it was pinned.
/// An empty file is still a note (it was pinned, maybe edited since).
pub fn read(path: &Path) -> Result<NoteBody, String> {
    let bytes = std::fs::read(path).map_err(|e| e.to_string())?;
    Ok(from_bytes(&bytes))
}

fn from_bytes(bytes: &[u8]) -> NoteBody {
    let bytes = bytes.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(bytes);
    let text = String::from_utf8_lossy(bytes);
    body(&text).unwrap_or(NoteBody { text: String::new(), link: None, domain: None, truncated: false })
}

/// Recycles leftover notes in `folder`, by the same rules as
/// [`capture::sweep`]: text files no print refers to, half-written ones,
/// nothing modified within [`capture::SWEEP_GRACE`] of `now`.
pub fn sweep(folder: &Path, pinned: &[PathBuf], now: SystemTime, bin: &dyn Recycler) -> Vec<PathBuf> {
    capture::sweep_where(folder, pinned, now, bin, is_note_file)
}

#[cfg(test)]
mod tests {
    use std::time::Duration;

    use super::*;
    use crate::recycle::testing::NotingBin;

    fn at() -> Timestamp {
        Timestamp { year: 2026, month: 10, day: 8, hour: 14, minute: 15, second: 30 }
    }

    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("tack-note-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn blank_text_is_not_a_note() {
        assert!(body("").is_none());
        assert!(body("  \r\n\t \n ").is_none());
        assert!(body("\0").is_none());
        assert!(body("\u{1}").is_none(), "a copied picture's placeholder alone is no text");
        assert_eq!(body("a\u{1}b\tc").unwrap().text, "ab\tc");
    }

    #[test]
    fn text_is_normalised_but_keeps_its_shape() {
        let note = body("\r\n  \r\n    fn main() {\r\n        go();\r\n    }\r\n\r\n  ").unwrap();
        assert_eq!(note.text, "    fn main() {\n        go();\n    }");
        assert!(!note.truncated);
        assert_eq!(note.link, None);
        assert_eq!(body("old\rmac").unwrap().text, "old\nmac");
    }

    #[test]
    fn long_text_is_cut_at_a_character_boundary() {
        // 3-byte characters, so the limit falls inside one.
        let text = "€".repeat(MAX_NOTE_BYTES / 3 + 10);
        let note = body(&text).unwrap();
        assert!(note.truncated);
        assert!(note.text.len() <= MAX_NOTE_BYTES);
        assert!(note.text.len() > MAX_NOTE_BYTES - 3);
        assert!(note.text.chars().all(|c| c == '€'));
        // Exactly at the limit is not truncated.
        let exact = "a".repeat(MAX_NOTE_BYTES);
        assert!(!body(&exact).unwrap().truncated);
        assert!(body(&format!("{exact}b")).unwrap().truncated);
    }

    #[test]
    fn a_lone_web_address_is_a_link() {
        let note = body("  https://www.GitHub.com/tauri-apps/tauri/releases?x=1#top \n").unwrap();
        assert_eq!(note.link.as_deref(), Some("https://www.GitHub.com/tauri-apps/tauri/releases?x=1#top"));
        assert_eq!(note.domain.as_deref(), Some("github.com"));
        assert_eq!(link_of("HTTP://example.org").as_deref(), Some("HTTP://example.org"));
        assert_eq!(link_of("www.example.org/a").as_deref(), Some("https://www.example.org/a"));
        assert_eq!(domain_of("https://user:pw@docs.rs:8080/x").as_deref(), Some("docs.rs"));
        assert_eq!(domain_of("http://[::1]:5178/").as_deref(), Some("[::1]"));
    }

    #[test]
    fn anything_else_is_plain_text() {
        for text in [
            "see https://example.org",
            "https://example.org and more",
            "file:///C:/Windows/System32/calc.exe",
            "mailto:someone@example.org",
            "javascript:alert(1)",
            "C:\\Users\\me\\notes.txt",
            "https://",
            "https:///path",
            "https://exa mple.org",
            "https://example.org/\"quoted\"",
            "example.org",
        ] {
            let note = body(text).unwrap();
            assert_eq!(note.link, None, "{text}");
            assert_eq!(note.domain, None, "{text}");
        }
        let long = format!("https://example.org/{}", "a".repeat(MAX_LINK_LEN));
        assert_eq!(link_of(&long), None);
    }

    #[test]
    fn notes_are_saved_with_windows_line_breaks_and_read_back() {
        let dir = scratch("save");
        let note = body("first line\nsecond line").unwrap();
        let first = save(&note, &dir, at()).unwrap();
        let second = save(&note, &dir, at()).unwrap();
        let raw = std::fs::read(&first).unwrap();
        let back = read(&first).unwrap();
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(first.file_name().unwrap(), "Note 2026-10-08 141530.txt");
        assert_eq!(second.file_name().unwrap(), "Note 2026-10-08 141530 (2).txt");
        assert_eq!(raw, b"first line\r\nsecond line");
        assert_eq!(back, note);
    }

    #[test]
    fn reading_tolerates_a_bom_bad_bytes_and_an_emptied_file() {
        let note = from_bytes(b"\xEF\xBB\xBFhttps://example.org\r\n");
        assert_eq!(note.link.as_deref(), Some("https://example.org"));
        let bad = from_bytes(b"caf\xE9");
        assert_eq!(bad.text, "caf\u{FFFD}");
        let empty = from_bytes(b"");
        assert_eq!(empty.text, "");
        assert!(!empty.truncated);
    }

    #[test]
    fn the_sweep_takes_old_unpinned_notes_only() {
        let dir = scratch("sweep");
        let file = |name: &str| {
            let path = dir.join(name);
            std::fs::write(&path, b"x").unwrap();
            path
        };
        let pinned = file("Note 2026-10-08 141530.txt");
        let leftover = file("Note 2026-10-07 101010.txt");
        let partial = file("Note 2026-10-07 101011.txt.part");
        file("not ours.png");
        let later = SystemTime::now() + Duration::from_secs(60 * 60);
        let bin = NotingBin::default();
        let mut swept = sweep(&dir, std::slice::from_ref(&pinned), later, &bin);
        let _ = std::fs::remove_dir_all(&dir);
        swept.sort();
        assert_eq!(swept, vec![leftover, partial]);
    }
}
