//! Captures: what a Snipping Tool image on the clipboard means for the board.
//! Each one is saved in Tack's captures folder and pinned, unless it repeats
//! the previous copy or is the twin of a screenshot file pinned moments ago.
//!
//! A new capture flies onto the board from where it was taken. Writing its
//! PNG can take most of a second, so nothing waits for it: as soon as the
//! picture is read, its place on screen is looked up
//! (`tack_windows::capture_origin`, on a thread of its own) while the flight
//! picture and the thumbnail are drawn, and the print is pinned under the
//! file's reserved name while the file is still being written in the
//! background. Both helper threads start with the app and wait for work:
//! starting a thread can stall for most of a second while the web view is
//! waking (loading its libraries), which is exactly when a capture lands. Anything that needs the file itself (copy, open, drag,
//! recycle...) waits for that write through [`when_saved`] or
//! [`wait_saved`].
//!
//! Also "Save to Pictures", which turns a capture into an ordinary
//! screenshot file that Tack no longer cleans up.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::mpsc::{self, Sender};
use std::sync::{Arc, Condvar, Mutex, OnceLock};
use std::time::{Duration, Instant};

use image::DynamicImage;
use tack_core::capture::{self, RepeatFilter};
use tack_core::{files, thumbnail, Origin, RevealReason, Thumb};
use tack_windows::capture_origin::{self, Lookup};
use tack_windows::{screenshots, snipping_tool};
use tauri::AppHandle;

use crate::ipc::events::{Flight, Removal};
use crate::prints::{self, Pin};
use crate::reveal::{self, Placement};
use crate::state::lock;

/// The longest anything waits for a capture's file to be written.
const SAVE_PATIENCE: Duration = Duration::from_secs(5);

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
/// A capture to write to its reserved path.
type SaveJob = (PathBuf, Arc<DynamicImage>);

static LOOKUPS: OnceLock<Sender<LookupJob>> = OnceLock::new();
static SAVES: OnceLock<Sender<SaveJob>> = OnceLock::new();

/// Starts the lookup and save threads (once).
fn start_workers(app: &AppHandle) {
    LOOKUPS.get_or_init(|| {
        worker("tack-origin", |(picture, pointer, reply): LookupJob| {
            let _ = reply.send(capture_origin::find(&picture, pointer));
        })
    });
    let app = app.clone();
    SAVES.get_or_init(move || {
        worker("tack-save", move |(path, img): SaveJob| {
            #[cfg(debug_assertions)]
            let started = Instant::now();
            let result = capture::write_reserved(&img, &path);
            drop(img);
            trace!("capture: file written in {:.0} ms", started.elapsed().as_secs_f64() * 1000.0);
            finish_save(&app, &path, result);
        })
    });
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
    if repeats.is_repeat(&img, Instant::now()) {
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
    let pictures = thumbnail::for_flight(&img);
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
        // copy Tack writes was never pinned or shown, and the same picture
        // sits in the user's own file, so it alone is removed outright
        // rather than recycled.
        Pin::Duplicate => {
            when_saved(&path, |path| {
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

// ---------------------------------------------------------------- writing in the background

/// Captures whose file is still being written, with what to do with each
/// once it is.
type Waiting = HashMap<PathBuf, Vec<Box<dyn FnOnce(&Path) + Send>>>;

static SAVING: Mutex<Option<Waiting>> = Mutex::new(None);
static SAVED: Condvar = Condvar::new();

fn saving() -> std::sync::MutexGuard<'static, Option<Waiting>> {
    SAVING.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Claims the capture's file name and has the save thread write it.
/// Returns the path it will have.
fn save_in_background(img: Arc<DynamicImage>) -> std::io::Result<PathBuf> {
    let path = capture::reserve(&capture::captures_folder(), snipping_tool::local_time())?;
    saving().get_or_insert_with(HashMap::new).insert(path.clone(), Vec::new());
    let sent = SAVES.get().is_some_and(|saves| saves.send((path.clone(), img)).is_ok());
    if !sent {
        saving().as_mut().map(|w| w.remove(&path));
        let _ = std::fs::remove_file(capture::partial_path(&path));
        return Err(std::io::Error::other("the save thread is not running"));
    }
    Ok(path)
}

fn finish_save(app: &AppHandle, path: &Path, result: std::io::Result<()>) {
    match result {
        // From now on only an edit of the file counts as a change.
        Ok(()) => {
            lock(app).board.refresh_stamp(path);
        }
        Err(e) => {
            eprintln!("tack: cannot save the capture {}: {e}", path.display());
            prints::remove_path(app, path, Removal::Quiet);
        }
    }
    let waiting = saving().as_mut().and_then(|w| w.remove(path)).unwrap_or_default();
    SAVED.notify_all();
    for then in waiting {
        then(path);
    }
}

/// Runs `then` with the file once it is written: straight away for any file
/// that is not a capture still being saved, else on the saving thread when
/// it is done. Never blocks.
pub fn when_saved(path: &Path, then: impl FnOnce(&Path) + Send + 'static) {
    {
        let mut saving = saving();
        if let Some(waiting) = saving.as_mut().and_then(|w| w.get_mut(path)) {
            waiting.push(Box::new(then));
            return;
        }
    }
    then(path);
}

/// The file is a capture still being written.
pub fn is_saving(path: &Path) -> bool {
    saving().as_ref().is_some_and(|w| w.contains_key(path))
}

/// Blocks until the file is written, if it is a capture still being saved
/// (for at most [`SAVE_PATIENCE`]). Off the UI thread only.
pub fn wait_saved(path: &Path) {
    let give_up = Instant::now() + SAVE_PATIENCE;
    let mut saving = saving();
    while saving.as_ref().is_some_and(|w| w.contains_key(path)) {
        let left = give_up.saturating_duration_since(Instant::now());
        if left.is_zero() {
            return;
        }
        saving = SAVED.wait_timeout(saving, left).unwrap_or_else(|p| p.into_inner()).0;
    }
}

// ---------------------------------------------------------------- Save to Pictures

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
        wait_saved(&path);
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

// ---------------------------------------------------------------- debug

/// Debug builds only: pins a generated picture through the same path a
/// Snipping Tool capture takes (saved, pinned, then the "new" reveal), so the
/// reveal can be exercised without touching the keyboard. `seed` varies the
/// pixels, so no two calls look like a repeat. No pointer, so no flight.
#[cfg(debug_assertions)]
pub fn simulate(app: &AppHandle, seed: u32) -> String {
    pin_capture(app, &mut RepeatFilter::new(), generated(seed), None)
}

/// Debug builds only: the same, in two steps, so a test can save the
/// capture ahead of time and then pin it at a moment of its choosing.
#[cfg(debug_assertions)]
pub fn simulate_saved(seed: u32) -> Result<impl FnOnce(&AppHandle) -> String, String> {
    let img = generated(seed);
    let path = capture::save(&img, &capture::captures_folder(), snipping_tool::local_time())
        .map_err(|e| format!("ignored (cannot save: {e})"))?;
    let thumb = thumbnail::from_image(img).map_err(|e| format!("ignored (cannot draw: {e})"))?;
    Ok(move |app: &AppHandle| pin_saved(app, path, thumb, Show::Reveal))
}

#[cfg(debug_assertions)]
fn generated(seed: u32) -> DynamicImage {
    let img = image::RgbImage::from_fn(640, 360, |x, y| {
        image::Rgb([(x / 3 + seed * 37) as u8, (y / 2 + seed * 91) as u8, (seed * 53 + (x ^ y)) as u8])
    });
    DynamicImage::ImageRgb8(img)
}

/// Debug builds only: a snip without Snipping Tool. Takes the real screen
/// inside `rect` (physical px) as the captured picture, puts the pointer on
/// `corner` of it, and checks that the origin lookup finds `rect` again,
/// also from a thread that is not DPI aware. With `fly`, the picture then
/// goes through the whole capture path from there (flight included). Logs
/// and returns a PASS or FAIL line.
#[cfg(debug_assertions)]
pub fn simulate_snip(app: &AppHandle, rect: tack_core::Rect, corner: tack_core::origin::Corner, fly: bool) -> String {
    use tack_core::origin::Corner;
    let Some(img) = capture_origin::grab_picture(rect) else {
        return format!("snip {rect:?} {corner:?}: FAIL (cannot grab the screen)");
    };
    let pointer = match corner {
        Corner::BottomRight => (rect.x + rect.w, rect.y + rect.h),
        Corner::TopLeft => (rect.x, rect.y),
        Corner::BottomLeft => (rect.x, rect.y + rect.h),
        Corner::TopRight => (rect.x + rect.w, rect.y),
    };
    let lookup = capture_origin::find(&img, pointer);
    let unaware = capture_origin::as_dpi_unaware_thread(|| capture_origin::find(&img, pointer));
    let ok = |l: &Lookup| l.found.is_some_and(|f| f.rect == rect);
    let line = format!(
        "snip {}x{} at ({}, {}), pointer on {corner:?}: {} found {:?} score {:.3} via {:?} in {} look(s), {:.1} ms CPU, {:.1} ms; from a DPI-unaware thread: {}",
        rect.w,
        rect.h,
        rect.x,
        rect.y,
        if ok(&lookup) && ok(&unaware) { "PASS" } else { "FAIL" },
        lookup.found.map(|f| f.rect),
        lookup.found.map_or(0.0, |f| f.score),
        lookup.found.map(|f| f.source),
        lookup.tries,
        lookup.cpu.as_secs_f64() * 1000.0,
        lookup.took.as_secs_f64() * 1000.0,
        if ok(&unaware) { "same" } else { "DIFFERENT" },
    );
    eprintln!("tack: {line}");
    if fly {
        let decision = pin_capture(app, &mut RepeatFilter::new(), img, Some(pointer));
        eprintln!("tack: simulated snip pinned: {decision}");
    }
    line
}
