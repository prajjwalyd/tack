//! What survives a restart: `%APPDATA%\Tack\board.json`, holding the pinned
//! prints in row order and the settings. Only paths are stored, never images
//! or a note's text; on startup each file is read again, and files that are
//! gone by then are skipped.
//!
//! The file carries a schema [`VERSION`]. Older layouts (version 2, without
//! notes, and the unversioned `prints` and `paths`/`captures` layouts) still
//! load. A file from a newer Tack is read as far as it goes but never trusted
//! for cleanup, and is backed up before this build writes over it.

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
const VERSION: u32 = 3;

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
        history::take_aged(&mut self.prints, |p| (p.kept_at, p.pinned_at), now_ms)
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
fn temp_file(file: &Path) -> PathBuf {
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
fn parse(bytes: &[u8], now_ms: u64, modified: impl Fn(&Path) -> Option<u64>) -> (Saved, Loaded) {
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
mod tests;
