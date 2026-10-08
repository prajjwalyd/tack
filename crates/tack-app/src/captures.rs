//! Captures: what a Snipping Tool image on the clipboard means for the board.
//! Each one is saved in Tack's captures folder and pinned, unless it repeats
//! the previous copy or is the twin of a screenshot file pinned moments ago.
//!
//! Also "Save to Pictures", which turns a capture into an ordinary
//! screenshot file that Tack no longer cleans up.

use std::path::PathBuf;
use std::time::Instant;

use image::DynamicImage;
use tack_core::capture::{self, RepeatFilter};
use tack_core::{files, thumbnail, Origin, RevealReason, Thumb};
use tack_windows::{screenshots, snipping_tool};
use tauri::AppHandle;

use crate::prints::{self, Pin};
use crate::reveal;
use crate::state::lock;

/// Starts listening to the clipboard.
pub fn start(app: AppHandle) {
    let mut repeats = RepeatFilter::new();
    snipping_tool::start(move |img| pin_capture(&app, &mut repeats, img));
}

/// Saves the capture and pins it. Returns the log's decision.
fn pin_capture(app: &AppHandle, repeats: &mut RepeatFilter, img: DynamicImage) -> String {
    if repeats.is_repeat(&img, Instant::now()) {
        return "duplicate (repeat)".into();
    }
    match save(img) {
        Ok((path, thumb)) => pin_saved(app, path, thumb),
        Err(decision) => decision,
    }
}

/// Writes the capture to the captures folder and draws its thumbnail. The
/// error is the log's decision.
fn save(img: DynamicImage) -> Result<(PathBuf, Thumb), String> {
    trace!("capture: saving {}x{}", img.width(), img.height());
    let path = match capture::save(&img, &capture::captures_folder(), snipping_tool::local_time()) {
        Ok(path) => path,
        Err(e) => return Err(format!("ignored (cannot save: {e})")),
    };
    trace!("capture: saved, drawing the thumbnail");
    match thumbnail::from_image(img) {
        Ok(thumb) => Ok((path, thumb)),
        Err(e) => {
            prints::discard_captures(vec![path]);
            Err(format!("ignored (cannot draw: {e})"))
        }
    }
}

/// Pins a saved capture and, if it is new, brings the board down.
fn pin_saved(app: &AppHandle, path: PathBuf, thumb: Thumb) -> String {
    trace!("capture: pinning");
    match prints::pin(app, path.clone(), thumb, Origin::Capture, prints::live()) {
        Pin::New(aged) => {
            reveal::reveal(app, RevealReason::New);
            // Queued behind the reveal, as for a new screenshot file.
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || aged.announce(&handle));
            "new".into()
        }
        // The auto-saved file got there first and is the one to keep. The
        // copy Tack wrote a moment ago was never pinned or shown, and the
        // same picture sits in the user's own file, so it alone is removed
        // outright rather than recycled.
        Pin::Duplicate => {
            if let Err(e) = std::fs::remove_file(&path) {
                eprintln!("tack: cannot remove the duplicate capture {}: {e}", path.display());
            }
            "duplicate".into()
        }
        // A merge pins the incoming file in its twin's place, so it stays.
        Pin::Merged => "merged".into(),
        Pin::Exists | Pin::Expired => {
            prints::discard_captures(vec![path]);
            "duplicate".into()
        }
    }
}

/// "Save to Pictures": the capture moves to the Screenshots folder and
/// becomes an ordinary file there, no longer Tack's to clean up.
pub fn save_to_pictures(app: &AppHandle, id: &str) {
    let path = {
        let s = lock(app);
        match s.board.find(id) {
            Some(print) if print.origin == Origin::Capture => print.path.clone(),
            _ => return,
        }
    };
    let (app, id) = (app.clone(), id.to_string());
    std::thread::spawn(move || {
        let dest = files::free_name(&screenshots::folder(), &path);
        // Repointed first, so the folder watcher sees a pinned file arrive
        // rather than a new screenshot.
        if prints::repoint(&app, &id, dest.clone(), Origin::Folder).is_none() {
            return;
        }
        if let Err(e) = files::move_file(&path, &dest) {
            eprintln!("tack: cannot save {} to {}: {e}", path.display(), dest.display());
            prints::repoint(&app, &id, path, Origin::Capture);
        }
    });
}

/// Debug builds only: pins a generated picture through the same path a
/// Snipping Tool capture takes (saved, pinned, then the "new" reveal), so the
/// reveal can be exercised without touching the keyboard. `seed` varies the
/// pixels, so no two calls look like a repeat.
#[cfg(debug_assertions)]
pub fn simulate(app: &AppHandle, seed: u32) -> String {
    pin_capture(app, &mut RepeatFilter::new(), generated(seed))
}

/// Debug builds only: the same, in two steps, so a test can save the
/// capture ahead of time and then pin it at a moment of its choosing.
#[cfg(debug_assertions)]
pub fn simulate_saved(seed: u32) -> Result<impl FnOnce(&AppHandle) -> String, String> {
    let (path, thumb) = save(generated(seed))?;
    Ok(move |app: &AppHandle| pin_saved(app, path, thumb))
}

#[cfg(debug_assertions)]
fn generated(seed: u32) -> DynamicImage {
    let img = image::RgbImage::from_fn(640, 360, |x, y| {
        image::Rgb([(x / 3 + seed * 37) as u8, (y / 2 + seed * 91) as u8, (seed * 53 + (x ^ y)) as u8])
    });
    DynamicImage::ImageRgb8(img)
}
