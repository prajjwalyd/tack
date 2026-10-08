//! A print: one screenshot or note pinned to the board, and where its file
//! came from. Tack points at screenshot files where they already are and
//! leaves them there; captures and notes are files Tack wrote itself, so
//! nobody else knows they are there and Tack cleans them up.

use std::path::PathBuf;
use std::time::{Instant, SystemTime};

use serde::Serialize;

/// One screenshot or note pinned to the board. Serialises as the IPC's
/// `Print` (see docs/ipc.md); the fields marked `skip` stay on this side.
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Print {
    /// Opaque and stable for the print's life.
    pub id: String,
    /// The file name, e.g. "Screenshot 2026-10-07 231455.png".
    pub name: String,
    /// A screenshot or a note.
    pub kind: Kind,
    /// A `data:image/jpeg` URL, long side at most 360 px. Empty for a note.
    pub thumb: String,
    /// The original size in pixels; 0 for a note.
    pub width: u32,
    pub height: u32,
    /// When it was first pinned, ms since the Unix epoch. Survives restarts;
    /// the UI shows it as "2 min ago", and the history ages prints by it.
    pub pinned_at: u64,
    /// Kept prints lead the row and never age out. Always equal to
    /// `kept_at.is_some()`; only [`crate::Board::set_kept`] changes either.
    pub kept: bool,
    /// When it was kept, ms since the Unix epoch.
    pub kept_at: Option<u64>,
    /// A note's text and link; `None` for a screenshot.
    pub note: Option<NoteBody>,
    #[serde(skip)]
    pub path: PathBuf,
    /// Modification time and size when the thumbnail was made, so an edit
    /// (Paint saving over the file) can be told apart from a stray event.
    #[serde(skip)]
    pub stamp: Option<(SystemTime, u64)>,
    #[serde(skip)]
    pub origin: Origin,
    /// When a live arrival reached the board, to spot the two halves of an
    /// auto-saved screenshot; None for prints restored at startup.
    #[serde(skip)]
    pub arrived: Option<Instant>,
}

impl Print {
    pub fn is_note(&self) -> bool {
        self.kind == Kind::Note
    }

    /// Tack wrote this print's file (a capture, a dropped image or a note),
    /// so Tack recycles it when the print leaves the board.
    pub fn owned(&self) -> bool {
        self.origin == Origin::Capture
    }
}

/// What a print shows. Serialised as `"image"` or `"note"`.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Kind {
    /// A screenshot (or another picture) as a photo print.
    #[default]
    Image,
    /// A short text or a link, on a small paper note.
    Note,
}

/// A note's contents, as the UI gets them (see [`crate::note`]).
#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
pub struct NoteBody {
    /// The text, at most [`crate::note::MAX_NOTE_BYTES`] of UTF-8, with
    /// `\n` line breaks.
    pub text: String,
    /// The URL, when the whole note is one web link (http or https).
    pub link: Option<String>,
    /// That link's host without "www.", for the note's caption.
    pub domain: Option<String>,
    /// The text was longer than the limit and was cut there.
    pub truncated: bool,
}

/// Where a print's file came from.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Origin {
    /// A screenshot file in the Screenshots folder (or anywhere else the user
    /// keeps it).
    Folder,
    /// A file Tack wrote itself: a Snipping Tool image from the clipboard
    /// or a picture dropped on the board, saved in
    /// [`crate::capture::captures_folder`], or a note's text file in
    /// [`crate::note::notes_folder`].
    Capture,
}
