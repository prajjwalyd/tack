//! The platform-independent heart of Tack: the board and the prints pinned to
//! it, how prints arrive and leave, what is saved between sessions, and the
//! thumbnails the UI shows. Nothing here knows about Tauri or Win32, so all of
//! it can be unit tested on its own; `tack-windows` and `tack-app` build the
//! actual app around it.
//!
//! The words used throughout: the **board** waits just above the top edge of
//! the screen; a **print** is one screenshot pinned to it with a **pin**; a
//! print comes either from a **screenshot file** in the Screenshots folder or
//! from a **capture**, a Snipping Tool image Tack read off the clipboard. A
//! **note** is a print of text or a link, pinned on purpose. The
//! board is **revealed** (slides down) and **tucked** (slides back up). A
//! print the user **keeps** stays until they unpin it; the others form the
//! **history**, and **age out** of it after a week or fifty newer prints.

pub mod board;
pub mod capture;
pub mod files;
pub mod history;
pub mod note;
pub mod origin;
pub mod print;
pub mod recycle;
pub mod settings;
pub mod shortcut;
pub mod store;
pub mod thumbnail;
pub mod view;

pub use board::{Arrival, Board, KeepChange, PinOutcome};
pub use print::{Kind, NoteBody, Origin, Print};
pub use settings::Settings;
pub use thumbnail::Thumb;
pub use view::{CssRect, Rect, RevealReason, View};
