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
const TWIN_WINDOW: Duration = Duration::from_secs(5);

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
    /// and hands them back, in row order.
    pub fn age_out(&mut self, now_ms: u64) -> Vec<Print> {
        history::take_aged(&mut self.prints, |p| (p.kept_at, p.pinned_at), now_ms)
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
mod tests;
