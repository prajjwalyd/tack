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
//! and once the suspend has gone through, trims tack.exe's own working set.
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
use std::time::Duration;

use tauri::{AppHandle, Manager};
use webview2_com::Microsoft::Web::WebView2::Win32::{
    ICoreWebView2, ICoreWebView2Controller, ICoreWebView2_19, ICoreWebView2_3, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL,
    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL,
};
use webview2_com::TrySuspendCompletedHandler;
use windows::core::{Interface, BOOL};
use windows::Win32::System::Memory::SetProcessWorkingSetSizeEx;
use windows::Win32::System::Threading::GetCurrentProcess;

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
    if let Err(e) = unsafe { webview3.TrySuspend(&handler) } {
        SUSPENDING.set(false);
        debug_log(format_args!("suspending the web view failed: {e}"));
        run_on_awake();
        return;
    }
    #[cfg(debug_assertions)]
    debug::suspend_requested();
}

/// Wakes the web view, then runs `then` once its page is running again:
/// straight away, unless a suspend is still pending, in which case as soon
/// as it has completed and been undone. Main thread only.
pub fn wake(app: &AppHandle, then: impl FnOnce() + 'static) {
    WANT_ASLEEP.set(false);
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

    /// Called right after the window is shown, with the moment the wake
    /// began. Logs how long the page took to run script again and to reach
    /// its next animation frame, which is when the slide starts drawing.
    pub fn probe_reveal(app: &AppHandle, started: Instant) {
        if std::env::var("TACK_DEBUG_CONTROL").as_deref() != Ok("1") {
            return;
        }
        // The page notes when the probe ran and when the next frame began,
        // both on its own clock, so only their difference is used.
        let script = "(() => { const t = performance.now(); window.__tackProbe = { t, frame: 0 }; \
                      requestAnimationFrame(() => { window.__tackProbe.frame = performance.now(); }); \
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
                    std::thread::sleep(Duration::from_millis(500));
                    let _ = app2.clone().run_on_main_thread(move || read_frame(&app2, script_ms));
                });
                Ok(())
            }));
            let _ = unsafe { webview.ExecuteScript(&HSTRING::from(script), &handler) };
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
                let gap: f64 = json.trim().parse().unwrap_or(f64::NAN);
                eprintln!(
                    "tack: reveal probe: script ran {script_ms:.1} ms after the wake, first frame ~{:.1} ms",
                    script_ms + gap
                );
                Ok(())
            }));
            let js = "window.__tackProbe && window.__tackProbe.frame ? \
                      window.__tackProbe.frame - window.__tackProbe.t : -1";
            let _ = unsafe { h.webview.ExecuteScript(&HSTRING::from(js), &handler) };
        });
    }
}
