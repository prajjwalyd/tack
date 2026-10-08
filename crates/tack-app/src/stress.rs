//! Debug builds only: a stress test for "a new capture brings the board
//! down", run from inside the app so it never needs the keyboard or the
//! pointer. Started through the debug control (docs/performance.md):
//!
//! - `stress` (the file may hold a cycle count, default 30): tuck, wait a
//!   while (from 0.2 s, inside the window where the web view is being put to
//!   sleep, up to 5 s), pin a generated capture, then check what the page
//!   reported back: the reveal handled within 300 ms of the capture, its
//!   print's pin-on started on schedule (half way through the slide, so
//!   within 400 ms of the reveal) and its sound played, and no other print
//!   pinned on with it.
//! - `stress-stale`: the same, but each capture arrives while the poller's
//!   last full-screen sample still says "busy", as it does for up to half a
//!   second after Snipping Tool's own full-screen overlay closes.
//! - `stress-pending`: the capture, saved ahead of time, is pinned on the
//!   main thread right after the web view's suspend has been asked for, so
//!   its reveal arrives while that suspend is still pending.
//! - `backlog`: three captures arrive while something really is full screen
//!   (so each is pinned quietly), then the tray brings the board down: only
//!   the newest of them should pin on with a sound.
//!
//! The page reports through [`debug_ack`], which it only calls once the
//! backend has set `window.__tackDebug` (see [`enable_page_acks`]).

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use tack_core::RevealReason;
use tauri::{AppHandle, Manager};

use crate::reveal::{self, WINDOW_LABEL};
use crate::state::lock;
use crate::{captures, trace, webview_power};

/// Something full screen is in front, as far as the reveal is concerned,
/// while this is set (the `backlog` run).
pub static FORCE_FULLSCREEN: AtomicBool = AtomicBool::new(false);

/// A run is under way: the pointer poller's reveals and tucks are ignored
/// meanwhile, so the person at the desk cannot disturb it (or be disturbed).
static RUNNING: AtomicBool = AtomicBool::new(false);

pub fn running() -> bool {
    RUNNING.load(Ordering::SeqCst)
}

/// Pauses between the tuck and the capture, cycled through. Most fall in or
/// near the sleep window: the window hides 300 ms after the tuck and the
/// suspend completes a little later.
const DELAYS_MS: [u64; 15] = [200, 280, 300, 310, 320, 340, 370, 420, 500, 650, 900, 1500, 2500, 3500, 5000];
/// How long a capture's page reports are waited for.
const SETTLE: Duration = Duration::from_millis(1300);
/// From the capture to the page handling the reveal.
const REVEAL_BUDGET_MS: f64 = 300.0;
/// From the reveal to the pin-on, which by design starts half way through
/// the slide.
const PIN_ON_BUDGET_MS: f64 = 400.0;

struct Ack {
    at: f64,
    what: String,
    id: Option<String>,
    queued: Option<u32>,
}

static ACKS: Mutex<Vec<Ack>> = Mutex::new(Vec::new());

/// The page did something worth checking: handled `board:reveal` (with
/// `queued` prints waiting to pin on), started a print's pin-on, or played
/// its sound.
#[tauri::command]
pub fn debug_ack(what: String, id: Option<String>, queued: Option<u32>) {
    let at = trace::now_ms();
    trace!("page: {what} id={} queued={}", id.as_deref().unwrap_or("-"), queued.map_or(-1, i64::from));
    if let Ok(mut acks) = ACKS.lock() {
        acks.push(Ack { at, what, id, queued });
    }
}

/// Asks the page to report back once it has loaded. Queued behind
/// everything else the page has been sent, so it holds even if the page is
/// asleep by then.
pub fn enable_page_acks(app: &AppHandle) {
    let app = app.clone();
    std::thread::spawn(move || {
        while !lock(&app).ui_ready {
            std::thread::sleep(Duration::from_millis(100));
        }
        if let Some(window) = app.get_webview_window(WINDOW_LABEL) {
            let _ = window.eval("window.__tackDebug = true");
        }
    });
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Run {
    Plain,
    StaleFullscreen,
    SuspendPending,
}

/// Runs `cycles` tuck-and-capture cycles and logs a verdict for each, then
/// a summary.
pub fn run(app: &AppHandle, cycles: usize, kind: Run) {
    RUNNING.store(true, Ordering::SeqCst);
    let mut failed = 0;
    let mut reveal_ms = Vec::new();
    for n in 0..cycles {
        let delay = DELAYS_MS[n % DELAYS_MS.len()];
        let verdict = match kind {
            Run::Plain | Run::StaleFullscreen => {
                reveal::tuck(app);
                std::thread::sleep(Duration::from_millis(delay));
                if kind == Run::StaleFullscreen {
                    // What the poller would have read while the snip overlay
                    // was up.
                    lock(app).view.fullscreen = true;
                }
                capture_and_check(|| (trace::now_ms(), pin_one(app, n as u32)))
            }
            Run::SuspendPending => match pending_cycle(app, n as u32) {
                Some(verdict) => verdict,
                None => continue,
            },
        };
        trace!("stress {kind:?} #{n}: tucked {delay} ms before the capture: {}", verdict.line);
        if verdict.ok {
            reveal_ms.push(verdict.reveal_ms);
        } else {
            failed += 1;
        }
    }
    reveal_ms.sort_by(f64::total_cmp);
    let median = reveal_ms.get(reveal_ms.len() / 2).copied().unwrap_or(f64::NAN);
    let max = reveal_ms.last().copied().unwrap_or(f64::NAN);
    trace!("stress {kind:?} done: {failed}/{cycles} failed; capture to reveal handled: median {median:.0} ms, max {max:.0} ms");
    RUNNING.store(false, Ordering::SeqCst);
}

/// Three captures while something is full screen, then the tray reveal.
pub fn backlog(app: &AppHandle) {
    RUNNING.store(true, Ordering::SeqCst);
    reveal::tuck(app);
    std::thread::sleep(Duration::from_millis(1500));
    FORCE_FULLSCREEN.store(true, Ordering::SeqCst);
    let mut ids = Vec::new();
    for n in 0..3 {
        lock(app).view.fullscreen = true;
        ids.push(pin_one(app, 1000 + n));
        std::thread::sleep(Duration::from_millis(400));
    }
    FORCE_FULLSCREEN.store(false, Ordering::SeqCst);
    lock(app).view.fullscreen = false;
    std::thread::sleep(Duration::from_millis(1500));
    clear_acks();
    reveal::reveal(app, RevealReason::Tray);
    std::thread::sleep(Duration::from_millis(1500));
    let acks = ACKS.lock().map(|a| a.iter().map(|a| (a.what.clone(), a.id.clone())).collect::<Vec<_>>());
    let acks = acks.unwrap_or_default();
    let pin_ons: Vec<_> = acks.iter().filter(|(w, _)| w == "pin-on").filter_map(|(_, id)| id.clone()).collect();
    let tocks = acks.iter().filter(|(w, _)| w == "tock").count();
    let newest = ids.last().cloned().flatten();
    let ok = pin_ons.len() == 1 && pin_ons.first() == newest.as_ref() && tocks == 1;
    trace!(
        "backlog: {} after 3 quiet captures: {} pin-on(s) {:?}, {tocks} sound(s); newest {}",
        if ok { "PASS" } else { "FAIL" },
        pin_ons.len(),
        pin_ons,
        newest.as_deref().unwrap_or("?")
    );
    RUNNING.store(false, Ordering::SeqCst);
}

struct Verdict {
    ok: bool,
    reveal_ms: f64,
    line: String,
}

fn clear_acks() {
    if let Ok(mut acks) = ACKS.lock() {
        acks.clear();
    }
}

/// Pins a generated capture; returns the new print's id.
fn pin_one(app: &AppHandle, seed: u32) -> Option<String> {
    let before = lock(app).board.order();
    let decision = captures::simulate(app, seed);
    let after = lock(app).board.order();
    trace!("simulated capture {seed}: {decision}");
    after.into_iter().find(|id| !before.contains(id))
}

/// Tucks a board that is down and pins a capture saved beforehand right
/// after the web view's suspend has been asked for.
fn pending_cycle(app: &AppHandle, seed: u32) -> Option<Verdict> {
    let pin = match captures::simulate_saved(seed) {
        Ok(pin) => pin,
        Err(e) => {
            trace!("stress: cannot save a capture: {e}");
            return None;
        }
    };
    if !lock(app).view.shown {
        reveal::reveal(app, RevealReason::Tray);
        std::thread::sleep(Duration::from_millis(1000));
    }
    let (tx, rx) = std::sync::mpsc::channel();
    let handle = app.clone();
    let hook = Box::new(move || {
        let t0 = trace::now_ms();
        let before = lock(&handle).board.order();
        let decision = pin(&handle);
        trace!("pre-saved capture {seed} pinned while the suspend is pending: {decision}");
        let id = lock(&handle).board.order().into_iter().find(|id| !before.contains(id));
        let _ = tx.send((t0, id));
    });
    if let Ok(mut slot) = webview_power::debug::ON_SUSPEND.lock() {
        *slot = Some(hook);
    }
    Some(capture_and_check(|| {
        reveal::tuck(app);
        rx.recv_timeout(Duration::from_secs(3)).unwrap_or_else(|_| {
            trace!("stress: the suspend was never asked for");
            if let Ok(mut slot) = webview_power::debug::ON_SUSPEND.lock() {
                *slot = None;
            }
            (trace::now_ms(), None)
        })
    }))
}

/// `pin` pins the capture and says when it started and the new print's id.
fn capture_and_check(pin: impl FnOnce() -> (f64, Option<String>)) -> Verdict {
    clear_acks();
    let (t0, id) = pin();
    std::thread::sleep(SETTLE);
    let acks = ACKS.lock();
    let Ok(acks) = acks.as_ref() else {
        return Verdict { ok: false, reveal_ms: f64::NAN, line: "FAIL: no acks".into() };
    };
    let reveal = acks.iter().find(|a| a.what == "reveal");
    let mine = acks.iter().find(|a| a.what == "pin-on" && a.id == id);
    let others = acks.iter().filter(|a| a.what == "pin-on" && a.id != id).count();
    let tock = acks.iter().any(|a| a.what == "tock" && a.id == id);
    let reveal_ms = reveal.map_or(f64::NAN, |a| a.at - t0);
    let pin_ms = match (reveal, mine) {
        (Some(r), Some(p)) => p.at - r.at,
        _ => f64::NAN,
    };
    let ok = reveal_ms <= REVEAL_BUDGET_MS && pin_ms <= PIN_ON_BUDGET_MS && others == 0 && tock;
    let line = format!(
        "{}: reveal handled +{reveal_ms:.0} ms (queued {}), pin-on +{pin_ms:.0} ms after it, sound {}, other pin-ons {others}",
        if ok { "PASS" } else { "FAIL" },
        reveal.and_then(|a| a.queued).map_or(-1, i64::from),
        if tock { "yes" } else { "no" },
    );
    Verdict { ok, reveal_ms, line }
}
