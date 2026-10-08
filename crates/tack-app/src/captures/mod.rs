//! Captures: Snipping Tool images on the clipboard. Each one is saved in
//! Tack's captures folder and pinned, unless it repeats the previous copy or
//! is the twin of a screenshot file pinned moments ago. A new capture flies
//! onto the board from where it was taken.
//!
//! Writing the PNG can take most of a second, so nothing waits for it: the
//! place on screen is looked up while the flight picture and thumbnail are
//! drawn, and the print is pinned while the file is written in the
//! background ([`saving`]). The helper threads start with the app because
//! starting a thread can stall while the web view is waking, which is
//! exactly when a capture lands.
//!
//! Also "Save to Pictures", which turns a capture into an ordinary
//! screenshot file that Tack no longer cleans up.

#[cfg(debug_assertions)]
mod debug;
mod saving;

use std::path::PathBuf;
use std::sync::mpsc::{self, Sender};
use std::sync::{Arc, OnceLock};
use std::time::Instant;

use image::DynamicImage;
use tack_core::capture::{self, RepeatFilter};
use tack_core::{files, thumbnail, Origin, RevealReason, Thumb};
use tack_windows::capture_origin::{self, Lookup};
use tack_windows::{screenshots, snipping_tool};
use tauri::AppHandle;

#[cfg(debug_assertions)]
pub use self::debug::{simulate, simulate_saved, simulate_snip};
use self::saving::save_in_background;
pub use self::saving::{is_saving, wait_saved, when_saved};
use crate::ipc::events::Flight;
use crate::prints::{self, Pin};
use crate::reveal::{self, Placement};
use crate::state::lock;

/// Starts listening to the clipboard, and the threads that help with each
/// capture.
pub fn start(app: AppHandle) {
    start_workers(&app);
    let mut repeats = RepeatFilter::new();
    snipping_tool::start(move |img, pointer| pin_capture(&app, &mut repeats, img, Some(pointer)));
}

/// Pins a picture copied from the selection (Win+Alt+C) like a capture:
/// saved in Tack's captures folder, pressed onto the board as it comes down.
/// Returns the log's decision.
pub fn pin_picture(app: &AppHandle, img: DynamicImage) -> String {
    pin_capture(app, &mut RepeatFilter::new(), img, None)
}

/// A capture's place on screen to look up, and where to send the answer.
type LookupJob = (Arc<DynamicImage>, (i32, i32), Sender<Lookup>);

static LOOKUPS: OnceLock<Sender<LookupJob>> = OnceLock::new();

/// Starts the lookup and save threads (once).
fn start_workers(app: &AppHandle) {
    LOOKUPS.get_or_init(|| {
        worker("tack-origin", |(picture, pointer, reply): LookupJob| {
            let _ = reply.send(capture_origin::find(&picture, pointer));
        })
    });
    saving::start_worker(app);
}

/// A thread that does `job` with everything sent to it, in order.
fn worker<T: Send + 'static>(name: &str, job: impl Fn(T) + Send + 'static) -> Sender<T> {
    let (tx, rx) = mpsc::channel::<T>();
    let spawned = std::thread::Builder::new().name(name.into()).spawn(move || {
        for item in rx {
            job(item);
        }
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot start {name}: {e}");
    }
    tx
}

/// How a pinned capture shows up.
enum Show {
    /// Flies onto the board from where it was taken, which comes down on
    /// that monitor.
    Fly(Placement, Flight),
    /// The board comes down for it as for any new screenshot (if nothing is
    /// full screen).
    Reveal,
    /// Pinned without bringing the board down: something full screen is in
    /// front.
    Quietly,
}

/// Pins the capture, flying it in from `pointer`'s snip if there is one.
/// Returns the log's decision.
fn pin_capture(app: &AppHandle, repeats: &mut RepeatFilter, img: DynamicImage, pointer: Option<(i32, i32)>) -> String {
    // Taken once: it spots a repeat now and a twin when the print is pinned.
    let hash = thumbnail::pixel_hash(&img);
    if repeats.is_repeat(hash, Instant::now()) {
        return "duplicate (repeat)".into();
    }
    #[cfg(debug_assertions)]
    let started = Instant::now();
    #[cfg(debug_assertions)]
    {
        let (w, h) = image::GenericImageView::dimensions(&img);
        trace!("capture: {w}x{h}, saving in the background");
    }
    let img = Arc::new(img);
    // Where it was taken, looked up while the pictures are drawn.
    let lookup = pointer.and_then(|pointer| {
        let (tx, rx) = mpsc::channel();
        LOOKUPS.get()?.send((img.clone(), pointer, tx)).ok()?;
        Some(rx)
    });
    let path = match save_in_background(img.clone()) {
        Ok(path) => path,
        Err(e) => return format!("ignored (cannot save: {e})"),
    };
    let pictures = thumbnail::for_flight(&img, hash);
    drop(img);
    let (image, thumb) = match pictures {
        Ok(pictures) => pictures,
        Err(e) => {
            prints::discard_captures(vec![path]);
            return format!("ignored (cannot draw: {e})");
        }
    };
    #[cfg(debug_assertions)]
    let drawn = started.elapsed();
    let lookup: Option<Lookup> = lookup.and_then(|rx| rx.recv().ok());
    let show = match lookup {
        Some(lookup) if reveal::new_may_reveal(app) => {
            let place = Placement::at(lookup.rect.center());
            let tip = !lock(app).settings.snip_tip_shown;
            let from = lookup.rect.to_css(place.pos, place.scale);
            trace!(
                "capture: flight from {:?} ({}), {} look(s), {:.1} ms CPU, found after {:.0} ms; pictures drawn after {:.0} ms",
                lookup.rect,
                lookup.found.map_or("not found, from the pointer".into(), |f| format!("{:?}, score {:.3}", f.source, f.score)),
                lookup.tries,
                lookup.cpu.as_secs_f64() * 1000.0,
                lookup.took.as_secs_f64() * 1000.0,
                drawn.as_secs_f64() * 1000.0,
            );
            Show::Fly(place, Flight { from, image, found: lookup.found.is_some(), tip })
        }
        Some(_) => Show::Quietly,
        None => Show::Reveal,
    };
    let decision = pin_saved(app, path, thumb, show);
    trace!("capture: {decision}, {:.0} ms after it was read", started.elapsed().as_secs_f64() * 1000.0);
    decision
}

/// Pins a capture (saved, or still being saved) and, if it is new, brings
/// the board down.
fn pin_saved(app: &AppHandle, path: PathBuf, thumb: Thumb, show: Show) -> String {
    let flight = match &show {
        Show::Fly(_, flight) => Some(flight),
        _ => None,
    };
    match prints::pin(app, path.clone(), thumb, Origin::Capture, prints::live(), flight) {
        Pin::New(aged) => {
            match show {
                Show::Fly(place, _) => reveal::show(app, RevealReason::New, place, reveal::FLIGHT_PEEK),
                Show::Reveal => reveal::reveal(app, RevealReason::New),
                Show::Quietly => {}
            }
            // Queued behind the reveal, as for a new screenshot file.
            let handle = app.clone();
            let _ = app.run_on_main_thread(move || aged.announce(&handle));
            "new".into()
        }
        // The auto-saved file got there first and is the one to keep. The
        // copy Tack writes was never pinned or shown, and the board found
        // the very same pixels (by their hash) in the user's own file, so it
        // alone is removed outright rather than recycled.
        Pin::Duplicate => {
            when_saved(&path, |path| {
                if !capture::in_captures(path) {
                    return;
                }
                if let Err(e) = std::fs::remove_file(path) {
                    eprintln!("tack: cannot remove the duplicate capture {}: {e}", path.display());
                }
            });
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
/// becomes an ordinary file there, no longer Tack's to clean up. It takes
/// a free name there and never replaces a file, even one that turns up
/// while it moves.
pub fn save_to_pictures(app: &AppHandle, id: &str) {
    let path = {
        let s = lock(app);
        match s.board.find(id) {
            Some(print) if print.origin == Origin::Capture => print.path.clone(),
            _ => return,
        }
    };
    let (app, id) = (app.clone(), id.to_string());
    let spawned = std::thread::Builder::new().name("tack-save-to-pictures".into()).spawn(move || {
        wait_saved(&path);
        let folder = screenshots::folder();
        // Repointed before each try, so the folder watcher sees a pinned
        // file arrive rather than a new screenshot.
        let moved = files::move_to_free_name(&path, &folder, |dest| {
            prints::repoint(&app, &id, dest.to_path_buf(), Origin::Folder).is_some()
        });
        if let Err(e) = moved {
            eprintln!("tack: cannot save {} to {}: {e}", path.display(), folder.display());
            prints::repoint(&app, &id, path, Origin::Capture);
        }
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot save to Pictures: {e}");
    }
}
