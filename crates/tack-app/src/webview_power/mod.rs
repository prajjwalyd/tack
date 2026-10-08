//! Puts the board's web view to sleep while the board is tucked, and wakes
//! it before the board comes down. Hiding the window is not enough: WebView2
//! keeps rendering and keeps its caches warm while its controller reports
//! itself visible.
//!
//! The COM objects are not `Send`, so they live in a thread local and every
//! function here must be called on the main thread.

#[cfg(debug_assertions)]
pub mod debug;
mod trim;

use std::cell::{Cell, RefCell};
use std::sync::atomic::Ordering;
use std::time::Duration;

use tauri::{AppHandle, Manager};
use webview2_com::Microsoft::Web::WebView2::Win32::{
    ICoreWebView2, ICoreWebView2Controller, ICoreWebView2_19, ICoreWebView2_3, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL,
    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL,
};
use webview2_com::TrySuspendCompletedHandler;
use windows::core::{Interface, BOOL};

use crate::reveal::WINDOW_LABEL;

/// The longest a reveal waits for a pending suspend to complete before it
/// resumes the page and goes ahead regardless.
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

/// Puts the hidden board's web view to sleep: memory target `Low` (the
/// runtime drops caches), controller invisible (Chromium frees compositor
/// layers), then `TrySuspend` (timers and script stop). Once suspended, the
/// working sets are trimmed. Main thread only.
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
                trim::own_working_set();
                HANDLES.with(|h| {
                    if let Some(h) = h.borrow().as_ref() {
                        trim::web_view_processes(&h.webview);
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

/// Wakes the web view, then runs `then` once its page is running again.
///
/// Anything sent to a sleeping page waits until it wakes, so the caller must
/// show the window and send `board:reveal` from `then`, not before. Usually
/// `then` runs at once; if a `TrySuspend` is still pending it runs once that
/// completes and is undone (or after [`SUSPEND_PATIENCE`]). Main thread only.
pub fn wake(app: &AppHandle, then: impl FnOnce() + 'static) {
    WANT_ASLEEP.set(false);
    trim::WAKES.fetch_add(1, Ordering::SeqCst);
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

/// Runs what [`wake`] put off. Each piece checks for itself whether it is
/// still wanted (the board may have been tucked again meanwhile).
fn run_on_awake() {
    let waiting = ON_AWAKE.with(|q| std::mem::take(&mut *q.borrow_mut()));
    for then in waiting {
        then();
    }
}

/// `MemoryUsageTargetLevel` needs WebView2 runtime 114; an older runtime
/// keeps its usual level.
fn set_memory_target(webview: &ICoreWebView2, level: COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL) {
    if let Ok(webview19) = webview.cast::<ICoreWebView2_19>() {
        let _ = unsafe { webview19.SetMemoryUsageTargetLevel(level) };
    }
}

/// How much of the sleep to do. Release builds always do all of it; debug
/// builds read `TACK_DEBUG_POWER` (off, hide, suspend or full).
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
