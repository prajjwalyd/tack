//! Revealing and tucking the board: updates the shared view, moves the
//! overlay window on the UI thread and tells the UI to slide. Also hands the
//! edge-reveal poller its way into the app.
//!
//! Every window operation runs on the main thread; touching the window from
//! the poller thread would mean cross-thread SendMessage calls that can
//! deadlock against the state lock.
//!
//! The web view sleeps while the board is tucked ([`webview_power`]): it is
//! put to sleep once the tuck slide is over and the window hidden. Every
//! reveal, whatever asked for it, wakes it first and waits until the page
//! runs again; only then is the window shown and the page told to slide down.

use std::time::{Duration, Instant};

use tack_core::view::{OVERHANG, STRIP_H};
use tack_core::{RevealReason, Settings, View};
use tack_windows::edge_reveal::EdgeHost;
use tack_windows::{overlay, pointer, snipping_tool, HWND};
use tauri::{AppHandle, Manager, WebviewWindowBuilder};

use crate::ipc::events;
use crate::state::lock;
use crate::{webview_power, webview_privacy};

/// The board window's label in tauri.conf.json.
pub const WINDOW_LABEL: &str = "board";
/// How long a new screenshot keeps the board down on its own.
const PEEK: Duration = Duration::from_secs(3);
/// The UI's tuck slide; the window hides once it is over.
const TUCK_HIDE_DELAY: Duration = Duration::from_millis(300);
/// After the UI first says it is ready, how long the hidden board's web view
/// stays awake to finish laying out and decoding the restored prints.
const FIRST_SLEEP_DELAY: Duration = Duration::from_millis(1500);
/// How long the empty window is shown once at startup (see
/// [`sleep_once_ready`]).
const WARM_UP: Duration = Duration::from_millis(400);
/// How long a new screenshot waits for a full-screen reading to clear
/// before its print is pinned without the board coming down, and how often
/// it asks meanwhile.
const FULLSCREEN_GRACE: Duration = Duration::from_millis(1000);
const FULLSCREEN_RECHECK: Duration = Duration::from_millis(100);

pub fn board_hwnd(app: &AppHandle) -> Option<HWND> {
    app.get_webview_window(WINDOW_LABEL)?.hwnd().ok()
}

/// Once at startup, on the main thread: creates the board window, makes sure
/// it never takes focus and never shows up in Alt+Tab or the taskbar, and
/// puts its web view to sleep once the UI is ready.
pub fn init_window(app: &AppHandle) {
    if app.get_webview_window(WINDOW_LABEL).is_none() {
        if let Err(e) = create_window(app) {
            // Without its window Tack is only a tray icon that does nothing;
            // fail the way a broken window config always has.
            eprintln!("tack: cannot create the board window: {e}");
            std::process::exit(1);
        }
    }
    if let Some(hwnd) = board_hwnd(app) {
        overlay::make_unfocusable(hwnd);
    }
    webview_privacy::apply(app);
    webview_power::attach(app);
    sleep_once_ready(app);
    #[cfg(debug_assertions)]
    webview_power::debug::start_control(app);
}

/// The window is described in tauri.conf.json but created here
/// (`"create": false` there), because only the builder can put WebView2's
/// user data folder in an absolute place: `%LOCALAPPDATA%\Tack\WebView2`,
/// next to Tack's other local data, rather than a folder named after the
/// bundle identifier.
fn create_window(app: &AppHandle) -> tauri::Result<()> {
    let Some(config) = app.config().app.windows.iter().find(|w| w.label == WINDOW_LABEL).cloned() else {
        return Err(tauri::Error::WindowNotFound);
    };
    let mut builder = WebviewWindowBuilder::from_config(app, &config)?;
    if let Ok(local) = app.path().local_data_dir() {
        builder = builder.data_directory(local.join("Tack").join("WebView2"));
    }
    builder.build()?;
    Ok(())
}

/// The window starts hidden, so its web view can sleep as soon as the UI has
/// loaded the board. Polls only until then.
///
/// Before it sleeps, the window is shown once, empty and click-through, for
/// a moment: the very first show of a WebView2 window sets up its surfaces
/// in the GPU process, which made the first reveal of a session take over
/// 100 ms. Nothing is drawn (the board is still tucked above the edge), so
/// this is invisible.
fn sleep_once_ready(app: &AppHandle) {
    let app = app.clone();
    std::thread::spawn(move || {
        let give_up = Instant::now() + Duration::from_secs(60);
        while !lock(&app).ui_ready {
            if Instant::now() > give_up {
                return;
            }
            std::thread::sleep(Duration::from_millis(250));
        }
        std::thread::sleep(FIRST_SLEEP_DELAY);
        let generation = lock(&app).view.generation;
        // Still tucked, and not revealed and tucked again meanwhile.
        let untouched = move |app: &AppHandle| {
            let s = lock(app);
            s.view.generation == generation && !s.view.shown
        };
        let handle = app.clone();
        let _ = app.run_on_main_thread(move || {
            if !untouched(&handle) {
                return;
            }
            let Some(hwnd) = board_hwnd(&handle) else { return };
            let mon = overlay::monitor_at(pointer::cursor_pos());
            let size = (mon.work.right - mon.work.left, ((STRIP_H + OVERHANG) * mon.scale).round() as i32);
            overlay::show_at(hwnd, (mon.work.left, mon.work.top), size);
        });
        std::thread::sleep(WARM_UP);
        let handle = app.clone();
        let _ = app.run_on_main_thread(move || {
            if !untouched(&handle) {
                return;
            }
            if let Some(hwnd) = board_hwnd(&handle) {
                overlay::hide(hwnd);
            }
            webview_power::sleep();
        });
    });
}

pub fn set_click_through(app: &AppHandle, ignore: bool) {
    let handle = app.clone();
    let _ = app.run_on_main_thread(move || {
        // Stale by the time it ran? The poller sends another one.
        if lock(&handle).view.ignoring != ignore {
            return;
        }
        if let Some(hwnd) = board_hwnd(&handle) {
            overlay::set_click_through(hwnd, ignore);
        }
    });
}

/// Slides the board down on the monitor under the pointer.
///
/// For a new screenshot this may first wait up to [`FULLSCREEN_GRACE`] (see
/// [`fullscreen_holds_back`]), so call it from a worker thread, as the
/// capture and screenshot-file code do.
pub fn reveal(app: &AppHandle, reason: RevealReason) {
    if reason == RevealReason::New && fullscreen_holds_back(app) {
        // The print is pinned all the same, and shows at the next reveal.
        return;
    }
    let pt = pointer::cursor_pos();
    let mon = overlay::monitor_at(pt);
    let now = Instant::now();
    let (generation, pos, size) = {
        let mut s = lock(app);
        trace!(
            "reveal {reason:?} requested: ui_ready={} shown={} fullscreen={} gen={}",
            s.ui_ready,
            s.view.shown,
            s.view.fullscreen,
            s.view.generation
        );
        if !s.ui_ready {
            return;
        }
        let v = &mut s.view;
        // Explicit requests always win; the edge does not interrupt a full
        // screen app (the poller has only just looked), and a new capture
        // has asked Windows afresh above.
        if reason == RevealReason::Edge && v.fullscreen {
            trace!("reveal {reason:?} skipped: full screen");
            return;
        }
        match reason {
            RevealReason::Hotkey | RevealReason::Tray => v.stay_open = true,
            RevealReason::New => v.peek_until = Some(now + PEEK),
            RevealReason::Edge => {}
        }
        if v.shown {
            trace!("reveal {reason:?}: already shown");
            return;
        }
        v.shown = true;
        v.generation += 1;
        v.reason = reason;
        v.entered = false;
        v.window_pos = (mon.work.left, mon.work.top);
        v.window_size = (mon.work.right - mon.work.left, ((STRIP_H + OVERHANG) * mon.scale).round() as i32);
        v.monitor_top = mon.monitor.top;
        v.scale = mon.scale;
        v.ignoring = true;
        (v.generation, v.window_pos, v.window_size)
    };

    let handle = app.clone();
    let _ = app.run_on_main_thread(move || {
        // Tucked again before this got to run.
        if lock(&handle).view.generation != generation {
            trace!("reveal gen={generation}: tucked again before it ran, skipped");
            return;
        }
        #[cfg(debug_assertions)]
        let started = Instant::now();
        // The page runs again before the window shows or hears anything, so
        // board:reveal is handled at once and the slide's first frame is
        // drawn by a running page.
        let app = handle.clone();
        webview_power::wake(&handle, move || {
            if lock(&app).view.generation != generation {
                trace!("reveal gen={generation}: tucked again while the page woke, skipped");
                return;
            }
            let Some(hwnd) = board_hwnd(&app) else { return };
            overlay::show_at(hwnd, pos, size);
            trace!("reveal gen={generation}: window shown");
            events::reveal(&app, reason);
            trace!("reveal gen={generation}: board:reveal sent");
            #[cfg(debug_assertions)]
            webview_power::debug::probe_reveal(&app, started);
        });
    });
}

/// Whether something full screen should keep a new screenshot's board up.
///
/// Asks Windows now rather than trusting the poller's last reading, which
/// can be half a second old: Win + Shift + S covers the screen with Snipping
/// Tool's own overlay until moments before the capture lands, and a reading
/// taken under it would hold back the very board the capture is for. That
/// overlay never counts. A reading that still says "full screen" gets
/// [`FULLSCREEN_GRACE`] to clear (a window closing, a transition) before the
/// answer is yes, and the print is pinned without the board coming down.
fn fullscreen_holds_back(app: &AppHandle) -> bool {
    let give_up = Instant::now() + FULLSCREEN_GRACE;
    loop {
        let fullscreen = overlay::fullscreen_app_in_front() || forced_fullscreen();
        let snipping = fullscreen && snipping_tool::in_front();
        {
            // The poller goes by this reading until it takes its next one.
            let mut s = lock(app);
            s.view.fullscreen = fullscreen;
            s.view.snipping = snipping;
        }
        if !fullscreen || snipping {
            return false;
        }
        if Instant::now() >= give_up {
            eprintln!(
                "tack: new screenshot pinned without revealing the board: full screen in front \
                 (notification state {}, app {})",
                overlay::notification_state(),
                snipping_tool::front_exe().as_deref().unwrap_or("unknown")
            );
            return true;
        }
        std::thread::sleep(FULLSCREEN_RECHECK);
    }
}

/// Debug builds: the stress test can make the screen count as full.
#[cfg(debug_assertions)]
fn forced_fullscreen() -> bool {
    crate::stress::FORCE_FULLSCREEN.load(std::sync::atomic::Ordering::SeqCst)
}

#[cfg(not(debug_assertions))]
fn forced_fullscreen() -> bool {
    false
}

/// Slides the board up, and hides the window once the slide is over.
pub fn tuck(app: &AppHandle) {
    let generation = {
        let mut s = lock(app);
        let v = &mut s.view;
        if !v.shown {
            return;
        }
        v.shown = false;
        v.generation += 1;
        v.stay_open = false;
        v.peek_until = None;
        v.edge_armed = false;
        v.generation
    };
    events::tuck(app);
    trace!("tuck gen={generation}: board:tuck sent");
    let handle = app.clone();
    std::thread::spawn(move || {
        std::thread::sleep(TUCK_HIDE_DELAY);
        let inner = handle.clone();
        let _ = handle.run_on_main_thread(move || {
            {
                let s = lock(&inner);
                if s.view.generation != generation || s.view.shown {
                    trace!("tuck gen={generation}: revealed again, window left up");
                    return;
                }
            }
            if let Some(hwnd) = board_hwnd(&inner) {
                overlay::hide(hwnd);
                trace!("tuck gen={generation}: window hidden");
                webview_power::sleep();
            }
        });
    });
}

/// The hotkey and the tray icon: down if it is up, up if it is down.
pub fn toggle(app: &AppHandle, reason: RevealReason) {
    if lock(app).view.shown {
        tuck(app);
    } else {
        reveal(app, reason);
    }
}

/// The edge-reveal poller's view of the app.
pub struct EdgeGlue(pub AppHandle);

impl EdgeHost for EdgeGlue {
    fn with_view<R>(&self, f: impl FnOnce(&mut View, &Settings) -> R) -> R {
        let mut s = lock(&self.0);
        let s = &mut *s;
        f(&mut s.view, &s.settings)
    }

    fn reveal(&self) {
        #[cfg(debug_assertions)]
        if crate::stress::running() {
            return;
        }
        reveal(&self.0, RevealReason::Edge);
    }

    fn tuck(&self) {
        #[cfg(debug_assertions)]
        if crate::stress::running() {
            return;
        }
        tuck(&self.0);
    }

    fn set_click_through(&self, ignore: bool) {
        set_click_through(&self.0, ignore);
    }

    fn pointer_left(&self) {
        events::pointer_left(&self.0);
    }
}
