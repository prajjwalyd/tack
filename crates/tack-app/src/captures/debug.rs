//! Debug builds only: captures without Snipping Tool, for the debug control
//! and the stress test.

use image::DynamicImage;
use tack_core::capture::{self, RepeatFilter};
use tack_core::origin::Corner;
use tack_core::{thumbnail, Rect};
use tack_windows::capture_origin::{self, Lookup};
use tack_windows::snipping_tool;
use tauri::AppHandle;

use super::{pin_capture, pin_saved, Show};

/// Pins a generated picture through the same path a Snipping Tool capture
/// takes (saved, pinned, then the "new" reveal). `seed` varies the pixels,
/// so no two calls look like a repeat. No pointer, so no flight.
pub fn simulate(app: &AppHandle, seed: u32) -> String {
    pin_capture(app, &mut RepeatFilter::new(), generated(seed), None)
}

/// The same, in two steps, so a test can save the capture ahead of time and
/// then pin it at a moment of its choosing.
pub fn simulate_saved(seed: u32) -> Result<impl FnOnce(&AppHandle) -> String, String> {
    let img = generated(seed);
    let path = capture::save(&img, &capture::captures_folder(), snipping_tool::local_time())
        .map_err(|e| format!("ignored (cannot save: {e})"))?;
    let thumb = thumbnail::from_image(img).map_err(|e| format!("ignored (cannot draw: {e})"))?;
    Ok(move |app: &AppHandle| pin_saved(app, path, thumb, Show::Reveal))
}

fn generated(seed: u32) -> DynamicImage {
    let img = image::RgbImage::from_fn(640, 360, |x, y| {
        image::Rgb([(x / 3 + seed * 37) as u8, (y / 2 + seed * 91) as u8, (seed * 53 + (x ^ y)) as u8])
    });
    DynamicImage::ImageRgb8(img)
}

/// A snip without Snipping Tool. Takes the real screen inside `rect`
/// (physical px) as the captured picture, puts the pointer on `corner` of
/// it, and checks that the origin lookup finds `rect` again, also from a
/// thread that is not DPI aware. With `fly`, the picture then goes through
/// the whole capture path (flight included). Logs and returns a PASS or
/// FAIL line.
pub fn simulate_snip(app: &AppHandle, rect: Rect, corner: Corner, fly: bool) -> String {
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
