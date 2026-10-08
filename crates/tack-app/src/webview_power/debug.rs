//! Debug builds only: lets a script reveal and tuck the board without the
//! keyboard or the pointer, and times how long a woken web view takes to
//! draw.

use std::path::PathBuf;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use tauri::{AppHandle, Manager};
use webview2_com::ExecuteScriptCompletedHandler;
use webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2_3;
use windows::core::HSTRING;

use super::HANDLES;
use crate::reveal::{self, WINDOW_LABEL};
use crate::stress::{self, Run};

/// Run once, on the main thread, right after the next `TrySuspend` has been
/// asked for: lets the stress test land a capture while that suspend is
/// still pending.
pub static ON_SUSPEND: Mutex<Option<Box<dyn FnOnce() + Send>>> = Mutex::new(None);

pub(super) fn suspend_requested() {
    let hook = ON_SUSPEND.lock().ok().and_then(|mut hook| hook.take());
    if let Some(hook) = hook {
        hook();
    }
}

/// With `TACK_DEBUG_CONTROL=1`, creating `%TEMP%\tack-debug\reveal` or
/// `...\tuck` reveals or tucks the board, as the tray icon would.
/// `capture` pins a generated capture through the real capture path, and
/// `stress`, `stress-stale`, `stress-pending` and `backlog` run the checks
/// in stress.rs (each `stress*` file may hold a cycle count).
pub fn start_control(app: &AppHandle) {
    if std::env::var("TACK_DEBUG_CONTROL").as_deref() != Ok("1") {
        return;
    }
    stress::enable_page_acks(app);
    let dir = std::env::temp_dir().join("tack-debug");
    let _ = std::fs::create_dir_all(&dir);
    let app = app.clone();
    let mut seed = 0;
    std::thread::spawn(move || loop {
        std::thread::sleep(Duration::from_millis(200));
        if take(dir.join("reveal")) {
            reveal::reveal(&app, tack_core::RevealReason::Tray);
        }
        if take(dir.join("tuck")) {
            reveal::tuck(&app);
        }
        if take(dir.join("ping")) {
            ping(&app);
        }
        if take(dir.join("capture")) {
            seed += 1;
            let decision = crate::captures::simulate(&app, 500 + seed);
            trace!("simulated capture: {decision}");
        }
        // `snip`: simulated snips of the real screen, every corner, a few
        // sizes; `snip-fly`: one more that flies onto the board. Either may
        // hold "x y w h [corner]" (physical px; corner br, tl, bl or tr) to
        // snip that rectangle instead.
        for (name, fly) in [("snip", false), ("snip-fly", true)] {
            let path = dir.join(name);
            let Ok(text) = std::fs::read_to_string(&path) else { continue };
            if std::fs::remove_file(&path).is_ok() {
                snips(&app, text.trim(), fly);
            }
        }
        for (name, run) in
            [("stress", Run::Plain), ("stress-stale", Run::StaleFullscreen), ("stress-pending", Run::SuspendPending)]
        {
            let path = dir.join(name);
            let Ok(text) = std::fs::read_to_string(&path) else { continue };
            if std::fs::remove_file(&path).is_ok() {
                stress::run(&app, text.trim().parse().unwrap_or(30), run);
            }
        }
        if take(dir.join("backlog")) {
            stress::backlog(&app);
        }
    });
}

/// Sends the page an event nobody listens to, then reports whether the web
/// view is still suspended: do events reaching a sleeping board wake it?
fn ping(app: &AppHandle) {
    use tauri::Emitter;
    let _ = app.emit("tack:debug-ping", ());
    std::thread::sleep(Duration::from_secs(1));
    let _ = app.run_on_main_thread(|| {
        HANDLES.with(|h| {
            let h = h.borrow();
            let Some(h) = h.as_ref() else { return };
            let mut suspended = windows::core::BOOL(0);
            if let Ok(w3) = windows::core::Interface::cast::<ICoreWebView2_3>(&h.webview) {
                let _ = unsafe { w3.IsSuspended(&mut suspended) };
            }
            eprintln!(
                "tack: after an event the web view is {}",
                if suspended.as_bool() { "still suspended" } else { "awake" }
            );
        });
    });
}

fn take(path: PathBuf) -> bool {
    std::fs::remove_file(path).is_ok()
}

/// Runs the simulated snips the `snip` and `snip-fly` files ask for.
fn snips(app: &AppHandle, spec: &str, fly: bool) {
    use tack_core::origin::Corner;
    use tack_core::Rect;
    let corner_of = |s: &str| match s {
        "tl" => Some(Corner::TopLeft),
        "bl" => Some(Corner::BottomLeft),
        "tr" => Some(Corner::TopRight),
        "br" => Some(Corner::BottomRight),
        _ => None,
    };
    let words: Vec<&str> = spec.split_whitespace().collect();
    let nums: Vec<i32> = words.iter().filter_map(|w| w.parse().ok()).collect();
    let corner = words.iter().find_map(|w| corner_of(w));
    let mon = tack_windows::overlay::monitor_at(tack_windows::pointer::cursor_pos());
    let (mw, mh) = (mon.monitor.right - mon.monitor.left, mon.monitor.bottom - mon.monitor.top);
    let rects: Vec<Rect> = if let [x, y, w, h, ..] = nums[..] {
        vec![Rect { x, y, w, h }]
    } else {
        // Small, medium and large, away from the board's strip at the top.
        vec![
            Rect { x: mon.monitor.left + mw * 3 / 8, y: mon.monitor.top + mh * 2 / 5, w: 240, h: 150 },
            Rect { x: mon.monitor.left + mw / 5, y: mon.monitor.top + mh / 4, w: mw * 2 / 5, h: mh / 3 },
            Rect { x: mon.monitor.left + mw / 10, y: mon.monitor.top + mh / 5, w: mw * 4 / 5, h: mh * 3 / 5 },
        ]
    };
    let corners: Vec<Corner> = match corner {
        Some(c) => vec![c],
        None if fly => vec![Corner::BottomRight],
        None => Corner::ALL.to_vec(),
    };
    let (mut passed, mut total) = (0, 0);
    for (i, rect) in rects.iter().enumerate() {
        for (j, &c) in corners.iter().enumerate() {
            let last = i + 1 == rects.len() && j + 1 == corners.len();
            let line = crate::captures::simulate_snip(app, *rect, c, fly && last);
            total += 1;
            if line.contains(": PASS") {
                passed += 1;
            }
        }
    }
    eprintln!("tack: simulated snips: {passed}/{total} PASS");
}

/// Called right after the window is shown, with the moment the wake began.
/// Logs how long the page took to run script again and to reach its next
/// animation frame, which is when the slide starts drawing.
pub fn probe_reveal(app: &AppHandle, started: Instant) {
    if std::env::var("TACK_DEBUG_CONTROL").as_deref() != Ok("1") {
        return;
    }
    watch_screen(app, started);
    // The page notes when the probe ran and when the next frame began, both
    // on its own clock, so only their difference is used.
    let script = "(() => { const t = performance.now(); const p = window.__tackProbe = { t, frame: 0, frames: [] }; \
                  const loop = () => { const n = performance.now() - t; p.frames.push(Math.round(n)); \
                  if (n < 1000) requestAnimationFrame(loop); }; \
                  requestAnimationFrame(() => { p.frame = performance.now(); loop(); }); \
                  return t; })()";
    let app = app.clone();
    HANDLES.with(|h| {
        let h = h.borrow();
        let Some(h) = h.as_ref() else { return };
        let webview = h.webview.clone();
        let handler = ExecuteScriptCompletedHandler::create(Box::new(move |_, _| {
            let script_ms = started.elapsed().as_secs_f64() * 1000.0;
            // Read the frame time back once it has surely happened.
            let app2 = app.clone();
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_millis(1200));
                let _ = app2.clone().run_on_main_thread(move || read_frame(&app2, script_ms));
            });
            Ok(())
        }));
        let _ = unsafe { webview.ExecuteScript(&HSTRING::from(script), &handler) };
    });
}

/// Watches a patch of the screen where the board comes down and logs when it
/// first changes: when the reveal really reached the screen, which can be
/// much later than the page's first animation frame after a long sleep.
fn watch_screen(app: &AppHandle, started: Instant) {
    let (pos, size, scale) = {
        let s = crate::state::lock(app);
        (s.view.window_pos, s.view.window_size, s.view.scale)
    };
    let patch = tack_core::Rect { x: pos.0 + size.0 / 2 - 20, y: pos.1 + (60.0 * scale) as i32, w: 40, h: 6 };
    std::thread::spawn(move || {
        let grab = || tack_windows::capture_origin::grab_picture(patch).map(|i| i.to_rgb8().into_raw());
        let Some(before) = grab() else { return };
        while started.elapsed() < Duration::from_millis(2500) {
            if let Some(now) = grab() {
                let diff: u64 =
                    now.iter().zip(&before).map(|(a, b)| (*a as i32 - *b as i32).unsigned_abs() as u64).sum();
                if diff > 40 * 6 * 3 * 12 {
                    eprintln!(
                        "tack: reveal probe: on screen {:.0} ms after the wake",
                        started.elapsed().as_secs_f64() * 1000.0
                    );
                    return;
                }
            }
        }
        eprintln!("tack: reveal probe: nothing on screen 2.5 s after the wake");
    });
}

fn read_frame(app: &AppHandle, script_ms: f64) {
    if app.get_webview_window(WINDOW_LABEL).is_none() {
        return;
    }
    HANDLES.with(|h| {
        let h = h.borrow();
        let Some(h) = h.as_ref() else { return };
        let handler = ExecuteScriptCompletedHandler::create(Box::new(move |_, json| {
            // "<gap> <frame times...>", as a JSON string.
            let text = json.trim().trim_matches('"').to_string();
            let mut words = text.split(' ');
            let gap: f64 = words.next().and_then(|g| g.parse().ok()).unwrap_or(f64::NAN);
            eprintln!(
                "tack: reveal probe: script ran {script_ms:.1} ms after the wake, first frame ~{:.1} ms; frames at +{}",
                script_ms + gap,
                words.collect::<Vec<_>>().join(",")
            );
            Ok(())
        }));
        let js = "window.__tackProbe && window.__tackProbe.frame ? \
                  (window.__tackProbe.frame - window.__tackProbe.t) + ' ' + window.__tackProbe.frames.join(' ') : '-1'";
        let _ = unsafe { h.webview.ExecuteScript(&HSTRING::from(js), &handler) };
    });
}
