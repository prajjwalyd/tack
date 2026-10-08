//! Where on screen a Snipping Tool capture was taken, so the new print can
//! fly up to the board from there. [`find`] grabs the screen around the
//! pointer and lets `tack_core::origin` pick the rectangle that shows the
//! picture, in physical pixels throughout. The grab is compared in memory and
//! freed at once; it is never saved, logged or sent anywhere.

use std::time::{Duration, Instant};

use image::{DynamicImage, GenericImageView};
use tack_core::origin::{self, Found, Pixels, Samples, FOCUS, PLAIN};
use tack_core::Rect;
use windows::Win32::Foundation::{HWND, POINT, RECT};
use windows::Win32::Graphics::Dwm::{DwmGetWindowAttribute, DWMWA_EXTENDED_FRAME_BOUNDS};
use windows::Win32::Graphics::Gdi::{
    BitBlt, CreateCompatibleDC, CreateDIBSection, DeleteDC, DeleteObject, GetDC, ReleaseDC, SelectObject, BITMAPINFO,
    BITMAPINFOHEADER, BI_RGB, DIB_RGB_COLORS, SRCCOPY,
};
use windows::Win32::System::Threading::{GetCurrentThread, GetThreadTimes};
use windows::Win32::UI::HiDpi::{
    SetThreadDpiAwarenessContext, DPI_AWARENESS_CONTEXT, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
    DPI_AWARENESS_CONTEXT_UNAWARE,
};
use windows::Win32::UI::WindowsAndMessaging::{GetAncestor, GetPhysicalCursorPos, WindowFromPhysicalPoint, GA_ROOT};

use crate::overlay;

/// Wait before the second look, if the first found nothing: Snipping Tool's
/// overlay may still be fading out when the capture arrives.
const RETRY_AFTER: Duration = Duration::from_millis(150);
/// Long side of the rectangle a print flies from when its picture was not
/// found on screen, in physical px at 100% (scaled with the monitor).
const FALLBACK_SIDE: f64 = 96.0;

/// The pointer, in physical pixels, whatever the calling thread's DPI
/// awareness.
pub fn pointer() -> (i32, i32) {
    let mut pt = POINT::default();
    unsafe {
        let _ = GetPhysicalCursorPos(&mut pt);
    }
    (pt.x, pt.y)
}

/// Samples `region` of the picture (its own coordinates) for comparing.
fn samples(img: &DynamicImage, region: Rect) -> Samples {
    let (w, h) = img.dimensions();
    Samples::within(w, h, region, |x, y| {
        let p = img.get_pixel(x, y).0;
        [p[0], p[1], p[2]]
    })
}

/// What [`find`] concluded.
#[derive(Clone, Copy, Debug)]
pub struct Lookup {
    /// Where the picture was found, if it was.
    pub found: Option<Found>,
    /// Where the print flies from: the place found, or else a small
    /// rectangle of the picture's shape centred on the pointer.
    pub rect: Rect,
    /// Looks taken: 1 (the quick look settled it) or 2.
    pub tries: u32,
    /// CPU time spent, and the time from start to answer (which includes the
    /// wait before a second look).
    pub cpu: Duration,
    pub took: Duration,
}

/// Finds where on screen `picture` was taken, with the pointer at `pointer`
/// (physical px) when the capture arrived. Blocks for up to two looks and
/// the wait between them, so call it off the UI thread.
pub fn find(picture: &DynamicImage, pointer: (i32, i32)) -> Lookup {
    let started = Instant::now();
    let cpu_before = thread_cpu();
    let (w, h) = picture.dimensions();
    // Physical coordinates for the grab and the window frame, whatever the
    // process's own awareness.
    let (found, tries) = with_thread_dpi(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, || {
        let found = look(picture, pointer, true);
        if !found.is_none_or(|f| f.detail < PLAIN) {
            return (found, 1);
        }
        // Nothing yet (wait for the overlay to go), or too plain to tell:
        // the whole picture this time.
        if found.is_none() {
            std::thread::sleep(RETRY_AFTER);
        }
        (look(picture, pointer, false).or(found), 2)
    });
    let rect = match found {
        Some(f) => f.rect,
        None => {
            let scale = overlay::monitor_at(POINT { x: pointer.0, y: pointer.1 }).scale;
            let long = (FALLBACK_SIDE * scale).round() as i32;
            origin::around_pointer(pointer, w as i32, h as i32, long)
        }
    };
    Lookup { found, rect, tries, cpu: thread_cpu().saturating_sub(cpu_before), took: started.elapsed() }
}

/// One look at the screen. Reading the screen costs more the more is read,
/// so `quick` reads only a square around the pointer and compares each
/// candidate by the part of the picture inside it; otherwise the whole
/// picture is compared.
fn look(picture: &DynamicImage, pointer: (i32, i32), quick: bool) -> Option<Found> {
    let mon = overlay::monitor_at(POINT { x: pointer.0, y: pointer.1 });
    let monitor = rect_of(mon.monitor);
    let (w, h) = picture.dimensions();
    let (w, h) = (w as i32, h as i32);
    let candidates = origin::candidates(pointer, w, h, window_under(pointer), Some(monitor));
    if quick {
        let area = origin::focus_area(pointer, FOCUS).intersect(&monitor)?;
        with_grab(area, |grab| {
            origin::find_each(grab, &candidates, |c| {
                origin::focus(pointer, c.rect, FOCUS).map(|region| samples(picture, region))
            })
        })
        .flatten()
    } else {
        let whole = samples(picture, Rect { x: 0, y: 0, w, h });
        let area = origin::search_area(&candidates)?.intersect(&monitor)?;
        with_grab(area, |grab| origin::find(&whole, grab, &candidates)).flatten()
    }
}

/// The top-level window under the point, by its visible frame.
fn window_under(pt: (i32, i32)) -> Option<Rect> {
    unsafe {
        let hwnd = WindowFromPhysicalPoint(POINT { x: pt.0, y: pt.1 });
        if hwnd.is_invalid() {
            return None;
        }
        let root = GetAncestor(hwnd, GA_ROOT);
        let root = if root.is_invalid() { hwnd } else { root };
        frame(root)
    }
}

unsafe fn frame(hwnd: HWND) -> Option<Rect> {
    let mut r = RECT::default();
    DwmGetWindowAttribute(
        hwnd,
        DWMWA_EXTENDED_FRAME_BOUNDS,
        &mut r as *mut RECT as *mut _,
        std::mem::size_of::<RECT>() as u32,
    )
    .ok()?;
    Some(rect_of(r))
}

fn rect_of(r: RECT) -> Rect {
    Rect { x: r.left, y: r.top, w: r.right - r.left, h: r.bottom - r.top }
}

/// A grab of the screen: 32-bit BGRA, top-down rows, in a DIB section that
/// lives only as long as [`with_grab`]'s callback.
struct Grab {
    area: Rect,
    bits: *const u8,
}

impl Pixels for Grab {
    fn rgb(&self, x: i32, y: i32) -> Option<[u8; 3]> {
        if !self.area.contains(x, y) {
            return None;
        }
        let (gx, gy) = ((x - self.area.x) as usize, (y - self.area.y) as usize);
        let i = (gy * self.area.w as usize + gx) * 4;
        // SAFETY: inside the area, so inside the DIB's w * h * 4 bytes.
        let p = unsafe { std::slice::from_raw_parts(self.bits.add(i), 3) };
        Some([p[2], p[1], p[0]])
    }
}

/// Copies `area` of the screen (physical px) into memory, hands it to `f`,
/// and frees it again. `None` if the screen could not be read.
fn with_grab<R>(area: Rect, f: impl FnOnce(&Grab) -> R) -> Option<R> {
    if area.w <= 0 || area.h <= 0 {
        return None;
    }
    unsafe {
        let screen = GetDC(None);
        if screen.is_invalid() {
            return None;
        }
        let mem = CreateCompatibleDC(Some(screen));
        let info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: area.w,
                // Negative: top-down rows.
                biHeight: -area.h,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0,
                ..Default::default()
            },
            ..Default::default()
        };
        let mut bits = std::ptr::null_mut();
        let result = match CreateDIBSection(Some(mem), &info, DIB_RGB_COLORS, &mut bits, None, 0) {
            Ok(dib) if !bits.is_null() => {
                let old = SelectObject(mem, dib.into());
                let copied = BitBlt(mem, 0, 0, area.w, area.h, Some(screen), area.x, area.y, SRCCOPY).is_ok();
                let out = copied.then(|| f(&Grab { area, bits: bits as *const u8 }));
                SelectObject(mem, old);
                let _ = DeleteObject(dib.into());
                out
            }
            _ => None,
        };
        let _ = DeleteDC(mem);
        ReleaseDC(None, screen);
        result
    }
}

/// CPU time (kernel + user) this thread has used so far.
fn thread_cpu() -> Duration {
    let (mut created, mut exited, mut kernel, mut user) = Default::default();
    let ok = unsafe { GetThreadTimes(GetCurrentThread(), &mut created, &mut exited, &mut kernel, &mut user) }.is_ok();
    if !ok {
        return Duration::ZERO;
    }
    let ticks = |t: windows::Win32::Foundation::FILETIME| ((t.dwHighDateTime as u64) << 32) | t.dwLowDateTime as u64;
    Duration::from_nanos((ticks(kernel) + ticks(user)) * 100)
}

/// Debug builds: the screen inside `area` as a picture, as Snipping Tool
/// would have captured it, for the debug control's simulated snips.
pub fn grab_picture(area: Rect) -> Option<DynamicImage> {
    let img = with_thread_dpi(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, || {
        with_grab(area, |grab| {
            image::RgbImage::from_fn(area.w as u32, area.h as u32, |x, y| {
                image::Rgb(grab.rgb(area.x + x as i32, area.y + y as i32).unwrap_or([0, 0, 0]))
            })
        })
    });
    img.map(DynamicImage::ImageRgb8)
}

/// Debug builds: runs `f` with the calling thread's DPI awareness set to
/// "unaware", where Windows scales coordinates to 96 DPI, to check that the
/// lookup still works in physical pixels.
pub fn as_dpi_unaware_thread<R>(f: impl FnOnce() -> R) -> R {
    with_thread_dpi(DPI_AWARENESS_CONTEXT_UNAWARE, f)
}

/// Runs `f` with the calling thread's DPI awareness set to `context`, then
/// puts the previous awareness back.
fn with_thread_dpi<R>(context: DPI_AWARENESS_CONTEXT, f: impl FnOnce() -> R) -> R {
    let previous = unsafe { SetThreadDpiAwarenessContext(context) };
    let out = f();
    if !previous.is_invalid() {
        unsafe { SetThreadDpiAwarenessContext(previous) };
    }
    out
}
