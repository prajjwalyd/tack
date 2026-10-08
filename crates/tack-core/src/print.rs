//! A print: one screenshot pinned to the board, and where its file came from.
//! Tack points at screenshot files where they already are and leaves them
//! there. Captures are different: Tack wrote those files itself, so it is
//! also responsible for deleting them.

use std::path::PathBuf;
use std::time::{Instant, SystemTime};

use serde::Serialize;

/// One screenshot pinned to the board. Serialises as the IPC's `Print` (see
/// docs/ipc.md); the fields marked `skip` stay on this side.
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Print {
    /// Opaque and stable for the print's life.
    pub id: String,
    /// The file name, e.g. "Screenshot 2026-10-07 231455.png".
    pub name: String,
    /// A `data:image/jpeg` URL, long side at most 360 px.
    pub thumb: String,
    /// The original size in pixels.
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

/// Where a print's file came from. Tack wrote a capture's file itself, so it
/// also cleans it up: nobody else knows it is there.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Origin {
    /// A screenshot file in the Screenshots folder (or anywhere else the user
    /// keeps it).
    Folder,
    /// A Snipping Tool image from the clipboard, saved in
    /// [`crate::capture::captures_folder`].
    Capture,
}
