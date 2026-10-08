//! Puts the board's web view to sleep while the board is tucked away, and
//! wakes it before the board comes down again.
//!
//! Tack spends nearly all day hidden, and hiding the window with `SW_HIDE` is
//! not enough for WebView2: it keeps rendering into its GPU surfaces and
//! keeps its caches warm as if it were on screen, because the controller
//! still reports itself visible. So once the tuck slide is over and the
//! window is hidden, [`sleep`]:
//!
//! 1. lowers the memory target to `Low`, which lets the runtime drop caches
//!    and trim what it can,
//! 2. marks the controller invisible, so Chromium treats the page as hidden
//!    and frees its compositor layers and tiles,
//! 3. asks for a suspend (`TrySuspend`), which stops the page's timers and
//!    script until it is needed again,
//!
//! and once the suspend has gone through, trims the working sets of tack.exe
//! and of the WebView2 processes (see [`trim_web_view_processes`]).
//!
//! [`wake`] undoes all three, and only once the page is running again does
//! it hand over to the caller, which then shows the window and tells the
//! page to slide down. Anything sent to a sleeping page waits and runs, in
//! order, once it wakes, so the order matters: a board shown, or told to
//! come down, before its page runs again is a board that does not move.
//!
//! `TrySuspend` completes asynchronously, so a reveal can arrive while a
//! suspend is still pending. The wake does not race it: the controller is
//! made visible at once, and the rest of the reveal waits for the suspend's
//! completion, which resumes the page if it did get suspended (or, should
//! the completion never come, for [`SUSPEND_PATIENCE`]). A tuck that comes
//! while a suspend is pending reuses it rather than asking for another.
//!
//! The COM objects are not `Send`, so they live in a thread local on the main
//! thread, and every function here must be called on the main thread (the
//! reveal and tuck code already runs window work there).

use std::cell::{Cell, RefCell};
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;

use tauri::{AppHandle, Manager};
use webview2_com::Microsoft::Web::WebView2::Win32::{
    ICoreWebView2, ICoreWebView2Controller, ICoreWebView2Environment8, ICoreWebView2_19, ICoreWebView2_2,
    ICoreWebView2_3, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW,
    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL,
};
use webview2_com::TrySuspendCompletedHandler;
use windows::core::{Interface, BOOL};
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::System::Memory::SetProcessWorkingSetSizeEx;
use windows::Win32::System::Threading::{
    GetCurrentProcess, OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SET_QUOTA,
};

use crate::reveal::WINDOW_LABEL;

/// The longest a reveal waits for a pending suspend to complete before it
/// resumes the page and goes ahead regardless. A suspend normally completes
/// within about 15 ms.
const SUSPEND_PATIENCE: Duration = Duration::from_millis(500);

/// The board's web view, once [`attach`] has fetched it.
struct Handles {
    controller: ICoreWebView2Controller,
    webview: ICoreWebView2,
}

thread_local! {
    static HANDLES: RefCell<Option<Handles>> = const { RefCell::new(None) };
    /// The board is tucked and the web view should be asleep. A pending
    /// `TrySuspend` reads it when it completes.
    static WANT_ASLEEP: Cell<bool> = const { Cell::new(false) };
    /// A `TrySuspend` has been asked for and has not completed yet.
    static SUSPENDING: Cell<bool> = const { Cell::new(false) };
    /// A wake came while that suspend was pending.
    static INTERRUPTED: Cell<bool> = const { Cell::new(false) };
    /// What [`wake`] was asked to do once the page runs, held while a
    /// suspend is pending.
    static ON_AWAKE: RefCell<Vec<Box<dyn FnOnce()>>> = const { RefCell::new(Vec::new()) };
}

/// Fetches the web view's controller and keeps it for [`sleep`] and
/// [`wake`]. `with_webview` queues the closure on the main thread, so this
/// may be called from anywhere, once the window exists.
pub fn attach(app: &AppHandle) {
    let Some(window) = app.get_webview_window(WINDOW_LABEL) else { return };
    let result = window.with_webview(|platform| {
        let controller = platform.controller();
        match unsafe { controller.CoreWebView2() } {
            Ok(webview) => HANDLES.with(|h| *h.borrow_mut() = Some(Handles { controller, webview })),
            Err(e) => eprintln!("tack: cannot reach the web view: {e}"),
        }
    });
    if let Err(e) = result {
        eprintln!("tack: cannot reach the web view: {e}");
    }
}

/// Puts the hidden board's web view to sleep. Main thread only.
pub fn sleep() {
    let mode = mode();
    if mode == Mode::Off {
        return;
    }
    HANDLES.with(|h| {
        let h = h.borrow();
        let Some(h) = h.as_ref() else {
            debug_log(format_args!("web view not attached yet, cannot sleep"));
            return;
        };
        debug_log(format_args!("web view going to sleep ({mode:?})"));
        WANT_ASLEEP.set(true);
        if mode >= Mode::Full {
            set_memory_target(&h.webview, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW);
        }
        // TrySuspend refuses a visible web view.
        if let Err(e) = unsafe { h.controller.SetIsVisible(false) } {
            debug_log(format_args!("hiding the web view failed: {e}"));
            return;
        }
        if mode < Mode::Suspend {
            return;
        }
        if SUSPENDING.get() {
            // Woken and tucked again before the last one completed; that one
            // now finds the board wanting to sleep and keeps it asleep.
            trace!("sleep: a suspend is still pending, no new one asked for");
            return;
        }
        request_suspend(h, mode);
    });
}

fn request_suspend(h: &Handles, mode: Mode) {
    let Ok(webview3) = h.webview.cast::<ICoreWebView2_3>() else { return };
    let resume_with = webview3.clone();
    let handler = TrySuspendCompletedHandler::create(Box::new(move |result, suspended| {
        SUSPENDING.set(false);
        let interrupted = INTERRUPTED.replace(false);
        let want_asleep = WANT_ASLEEP.get();
        trace!(
            "TrySuspend completed: ok={} suspended={suspended} want_asleep={want_asleep} interrupted={interrupted}",
            result.is_ok()
        );
        if !want_asleep {
            // Revealed while the suspend was pending: the page has to run
            // before the reveal goes on.
            if suspended {
                let _ = unsafe { resume_with.Resume() };
            }
        } else if suspended {
            if mode >= Mode::Full {
                trim_own_working_set();
                HANDLES.with(|h| {
                    if let Some(h) = h.borrow().as_ref() {
                        trim_web_view_processes(&h.webview);
                    }
                });
            }
        } else if interrupted {
            // A wake made the page visible mid-suspend, which spoiled it, and
            // a tuck has hidden it again since: ask once more.
            HANDLES.with(|h| {
                if let Some(h) = h.borrow().as_ref() {
                    request_suspend(h, mode);
                }
            });
        }
        debug_log(format_args!(
            "web view {}",
            match (result, suspended) {
                (Ok(()), true) if want_asleep => "suspended",
                (Ok(()), true) => "suspended, then resumed for a reveal",
                _ => "not suspended",
            }
        ));
        run_on_awake();
        Ok(())
    }));
    SUSPENDING.set(true);
    trace!("web view invisible, TrySuspend requested");
    let requested = unsafe { webview3.TrySuspend(&handler) };
    if let Err(e) = &requested {
        SUSPENDING.set(false);
        debug_log(format_args!("suspending the web view failed: {e}"));
        run_on_awake();
    }
    #[cfg(debug_assertions)]
    if requested.is_ok() {
        debug::suspend_requested();
    }
}

/// Wakes the web view, then runs `then` once its page is running again:
/// straight away, unless a suspend is still pending, in which case as soon
/// as it has completed and been undone. Main thread only.
pub fn wake(app: &AppHandle, then: impl FnOnce() + 'static) {
    WANT_ASLEEP.set(false);
    WAKES.fetch_add(1, Ordering::SeqCst);
    HANDLES.with(|h| {
        let h = h.borrow();
        let Some(h) = h.as_ref() else { return };
        // Visible again resumes a suspended page on its own; Resume covers
        // a runtime that has not done so yet.
        let _ = unsafe { h.controller.SetIsVisible(true) };
        resume_if_suspended(h);
        set_memory_target(&h.webview, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL);
    });
    if !SUSPENDING.get() {
        trace!("wake: page running");
        then();
        return;
    }
    trace!("wake: a suspend is still pending, the reveal waits for it");
    INTERRUPTED.set(true);
    ON_AWAKE.with(|q| q.borrow_mut().push(Box::new(then)));
    let app = app.clone();
    std::thread::spawn(move || {
        std::thread::sleep(SUSPEND_PATIENCE);
        let _ = app.run_on_main_thread(|| {
            if ON_AWAKE.with(|q| q.borrow().is_empty()) {
                return;
            }
            trace!("wake: the pending suspend never completed, resuming anyway");
            HANDLES.with(|h| {
                if let Some(h) = h.borrow().as_ref() {
                    resume_if_suspended(h);
                }
            });
            run_on_awake();
        });
    });
}

fn resume_if_suspended(h: &Handles) {
    let Ok(webview3) = h.webview.cast::<ICoreWebView2_3>() else { return };
    let mut suspended = BOOL(0);
    if unsafe { webview3.IsSuspended(&mut suspended) }.is_ok() && suspended.as_bool() {
        trace!("wake: page still suspended, resuming it");
        let _ = unsafe { webview3.Resume() };
    }
}

/// Runs what [`wake`] put off, now that the page runs. Each piece checks
/// for itself whether it is still wanted (the board may have been tucked
/// again meanwhile).
fn run_on_awake() {
    let waiting = ON_AWAKE.with(|q| std::mem::take(&mut *q.borrow_mut()));
    for then in waiting {
        then();
    }
}

/// `MemoryUsageTargetLevel` arrived in WebView2 runtime 114; an older
/// runtime simply keeps its usual level.
fn set_memory_target(webview: &ICoreWebView2, level: COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL) {
    if let Ok(webview19) = webview.cast::<ICoreWebView2_19>() {
        let _ = unsafe { webview19.SetMemoryUsageTargetLevel(level) };
    }
}

/// Hands the pages tack.exe is not using back to Windows: after startup and
/// a reveal, most of what it touched (image decoding, the window code) will
/// not be needed until the next one. Only the working set shrinks; pages
/// still in use come straight back from the standby list.
fn trim_own_working_set() {
    // Both sizes at usize::MAX means "remove as many pages as possible".
    let _ = unsafe { SetProcessWorkingSetSizeEx(GetCurrentProcess(), usize::MAX, usize::MAX, Default::default()) };
}

/// Trims the WebView2 processes' working sets, now and once more after
/// [`SECOND_TRIM`], unless the board is revealed meanwhile.
///
/// Left to itself the runtime pages a sleeping web view out only about 30 s
/// after the suspend: until then the GPU, renderer and browser processes
/// keep 50 to 120 MB in RAM, so Tack looks far bigger than it is to anyone
/// who glances at Task Manager after a tuck. Trimming them at once does
/// what that does, sooner. The GPU and browser processes are still tidying
/// up for a few seconds after the page went to sleep and touch some of it
/// again, hence the second pass. Only RAM is handed back (the pages go to
/// the standby list, from where a reveal takes them straight back); private
/// bytes do not change. See docs/performance.md, "Paging out at once".
fn trim_web_view_processes(webview: &ICoreWebView2) {
    // Debug builds: TACK_DEBUG_TRIM=off leaves it to the runtime, to compare.
    if cfg!(debug_assertions) && std::env::var("TACK_DEBUG_TRIM").as_deref() == Ok("off") {
        return;
    }
    let processes = web_view_processes(webview);
    for p in &processes {
        p.trim();
    }
    let wakes = WAKES.load(Ordering::SeqCst);
    std::thread::spawn(move || {
        std::thread::sleep(SECOND_TRIM);
        if WAKES.load(Ordering::SeqCst) == wakes {
            for p in &processes {
                p.trim();
            }
            trace!("web view processes trimmed again");
        }
    });
}

/// How long after the suspend the WebView2 processes are trimmed again.
const SECOND_TRIM: Duration = Duration::from_secs(4);

/// Counts wakes, so a delayed trim can tell the board has been revealed
/// since it was planned.
static WAKES: AtomicU64 = AtomicU64::new(0);

/// A WebView2 process, held open (so its id cannot be reused) until dropped.
struct Process(HANDLE);

// A process handle may be used, and closed, from any thread.
unsafe impl Send for Process {}

impl Process {
    fn trim(&self) {
        // Both sizes at usize::MAX means "remove as many pages as possible".
        let _ = unsafe { SetProcessWorkingSetSizeEx(self.0, usize::MAX, usize::MAX, Default::default()) };
    }
}

impl Drop for Process {
    fn drop(&mut self) {
        let _ = unsafe { CloseHandle(self.0) };
    }
}

/// The web view's processes (browser, GPU, renderer, utilities), as the
/// runtime lists them (`GetProcessInfos`, runtime 1.0.1072 or later).
fn web_view_processes(webview: &ICoreWebView2) -> Vec<Process> {
    let mut processes = Vec::new();
    let Ok(webview2) = webview.cast::<ICoreWebView2_2>() else { return processes };
    let Ok(env) = (unsafe { webview2.Environment() }) else { return processes };
    let Ok(env8) = env.cast::<ICoreWebView2Environment8>() else { return processes };
    let Ok(infos) = (unsafe { env8.GetProcessInfos() }) else { return processes };
    let mut count = 0;
    let _ = unsafe { infos.Count(&mut count) };
    for i in 0..count {
        let Ok(info) = (unsafe { infos.GetValueAtIndex(i) }) else { continue };
        let mut pid = 0;
        if unsafe { info.ProcessId(&mut pid) }.is_err() {
            continue;
        }
        let access = PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION;
        if let Ok(handle) = unsafe { OpenProcess(access, false, pid as u32) } {
            processes.push(Process(handle));
        }
    }
    processes
}

/// How much of the sleep to do. Release builds always do all of it; debug
/// builds read `TACK_DEBUG_POWER` (off, hide, suspend or full) so each step
/// can be measured on its own.
#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Debug)]
#[cfg_attr(not(debug_assertions), allow(dead_code))]
enum Mode {
    Off,
    Hide,
    Suspend,
    Full,
}

#[cfg(not(debug_assertions))]
fn mode() -> Mode {
    Mode::Full
}

#[cfg(debug_assertions)]
fn mode() -> Mode {
    static MODE: std::sync::OnceLock<Mode> = std::sync::OnceLock::new();
    *MODE.get_or_init(|| match std::env::var("TACK_DEBUG_POWER").as_deref() {
        Ok("off") => Mode::Off,
        Ok("hide") => Mode::Hide,
        Ok("suspend") => Mode::Suspend,
        _ => Mode::Full,
    })
}

fn debug_log(args: std::fmt::Arguments) {
    if cfg!(debug_assertions) {
        eprintln!("tack: {args}");
    }
}

/// Debug builds only: lets a script reveal and tuck the board without
/// touching the keyboard or the pointer, and times how long a woken web view
/// takes to draw. See docs/performance.md.
#[cfg(debug_assertions)]
pub mod debug {
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

    /// Run once, on the main thread, right after the next `TrySuspend` has
    /// been asked for: lets the stress test land a capture while that
    /// suspend is still pending.
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
    /// `stress`, `stress-stale`, `stress-pending` and `backlog` run the
    /// checks in stress.rs (each `stress*` file may hold a cycle count).
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
            // `snip`: simulated snips of the real screen, every corner, a
            // few sizes; `snip-fly`: one more that flies onto the board.
            // Either may hold "x y w h [corner]" (physical px; corner br,
            // tl, bl or tr) to snip that rectangle instead.
            for (name, fly) in [("snip", false), ("snip-fly", true)] {
                let path = dir.join(name);
                let Ok(text) = std::fs::read_to_string(&path) else { continue };
                if std::fs::remove_file(&path).is_ok() {
                    snips(&app, text.trim(), fly);
                }
            }
            for (name, run) in [
                ("stress", Run::Plain),
                ("stress-stale", Run::StaleFullscreen),
                ("stress-pending", Run::SuspendPending),
            ] {
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

    /// Sends the page an event nobody listens to, then reports whether the
    /// web view is still suspended: do events reaching a sleeping board wake
    /// it up?
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

    /// Called right after the window is shown, with the moment the wake
    /// began. Logs how long the page took to run script again and to reach
    /// its next animation frame, which is when the slide starts drawing.
    pub fn probe_reveal(app: &AppHandle, started: Instant) {
        if std::env::var("TACK_DEBUG_CONTROL").as_deref() != Ok("1") {
            return;
        }
        watch_screen(app, started);
        // The page notes when the probe ran and when the next frame began,
        // both on its own clock, so only their difference is used.
        let script =
            "(() => { const t = performance.now(); const p = window.__tackProbe = { t, frame: 0, frames: [] }; \
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

    /// Watches a patch of the screen where the board comes down and logs
    /// when it first changes: when the reveal really reached the screen,
    /// which can be much later than the page's first animation frame after
    /// a long sleep.
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
}
