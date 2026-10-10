//! Revealing and tucking the board: updates the shared view, moves the
//! overlay window and tells the UI to slide. Also the board window's setup
//! and the edge-reveal poller's way into the app.
//!
//! Every window operation runs on the main thread; touching the window from
//! the poller thread would mean cross-thread SendMessage calls that can
//! deadlock against the state lock. The web view sleeps while the board is
//! tucked ([`webview_power`]), so every reveal wakes it before showing the
//! window.

use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::time::{Duration, Instant};

use tack_core::{Rect, RevealReason, RevealSpeed, Settings, View};
use tack_windows::edge_reveal::{self, EdgeHost};
use tack_windows::{overlay, pointer, snipping_tool, HWND};
use tauri::{AppHandle, Manager, WebviewWindowBuilder};
use windows::Win32::Foundation::POINT;

use crate::ipc::events;
use crate::state::lock;
use crate::{settings_window, webview_power, webview_privacy};

/// The board window's label in tauri.conf.json.
pub const WINDOW_LABEL: &str = "board";
// How long a new screenshot, a capture that flies in or a notice keeps the
// board down on its own, how long the poller waits after the pointer leaves
// and how long the window waits to hide after a tuck all follow the user's
// speed: see `RevealSpeed` (peek, flight_peek, notice_peek, leave_delay,
// tuck_hide_delay), which also says which timers never scale. The ones below
// are class C or D.
/// How long the one-time tip under the board keeps it down for reading,
/// unless the pointer comes over it (and then leaves) first. Class D: text
/// to read, so it is the same at every speed.
const TIP_PEEK: Duration = Duration::from_secs(9);
/// After the UI first says it is ready, how long the hidden board's web view
/// stays awake to finish laying out and decoding the restored prints. Class C
/// (invisible startup work), like [`WARM_UP`] and the full-screen waits.
const FIRST_SLEEP_DELAY: Duration = Duration::from_millis(1500);
/// How long the window is shown once at startup, the board drawn unseen
/// (see [`sleep_once_ready`]).
const WARM_UP: Duration = Duration::from_millis(900);
/// How long a new screenshot waits for a full-screen reading to clear
/// before its print is pinned without the board coming down, and how often
/// it asks meanwhile.
const FULLSCREEN_GRACE: Duration = Duration::from_millis(1000);
const FULLSCREEN_RECHECK: Duration = Duration::from_millis(100);

/// Where the board's window goes on a monitor: its whole work area. The
/// window is transparent and click-through except on the board; being that
/// tall lets a capture fly in from anywhere without a resize (which can
/// flash), and stopping short of the taskbar keeps Windows from taking it
/// for a full-screen app.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Placement {
    /// Top-left and size, physical px.
    pub pos: (i32, i32),
    pub size: (i32, i32),
    /// Top of the monitor (not the work area).
    pub monitor_top: i32,
    /// The monitor's DPI / 96.
    pub scale: f64,
}

impl Placement {
    /// On the monitor holding `pt` (physical px), or the nearest one.
    pub fn at(pt: (i32, i32)) -> Placement {
        let mon = overlay::monitor_at(POINT { x: pt.0, y: pt.1 });
        let work = mon.work;
        let mut height = work.bottom - work.top;
        // Debug builds: TACK_DEBUG_WINDOW=strip limits it to a 396 CSS px
        // strip, to compare what the height costs.
        if cfg!(debug_assertions) && std::env::var("TACK_DEBUG_WINDOW").as_deref() == Ok("strip") {
            height = height.min((396.0 * mon.scale).round() as i32);
        }
        Placement {
            pos: (work.left, work.top),
            size: (work.right - work.left, height),
            monitor_top: mon.monitor.top,
            scale: mon.scale,
        }
    }

    /// On the monitor under the pointer.
    pub fn under_pointer() -> Placement {
        let pt = pointer::cursor_pos();
        Placement::at((pt.x, pt.y))
    }
}

/// The user's speed setting, as it is now (a short look at the state).
pub fn speed(app: &AppHandle) -> RevealSpeed {
    lock(app).settings.reveal
}

/// The monitor the board is down on, or will come down on (under the
/// pointer, as a reveal picks it).
pub fn board_placement(app: &AppHandle) -> Placement {
    let down_at = {
        let s = lock(app);
        s.view.shown.then_some(s.view.window_pos)
    };
    match down_at {
        Some(pos) => Placement::at(pos),
        None => Placement::under_pointer(),
    }
}

/// The board window's handle, kept for threads that must not wait on the
/// main thread (its getters go through the event loop).
static BOARD: AtomicIsize = AtomicIsize::new(0);

pub fn board_hwnd(app: &AppHandle) -> Option<HWND> {
    app.get_webview_window(WINDOW_LABEL)?.hwnd().ok()
}

/// Once at startup, on the main thread: creates the board window, makes sure
/// it never takes focus and never shows up in Alt+Tab or the taskbar, and
/// puts its web view to sleep once the UI is ready.
pub fn init_window(app: &AppHandle) {
    if app.get_webview_window(WINDOW_LABEL).is_none() {
        if let Err(e) = create_window(app) {
            // Without its window Tack is only a tray icon that does nothing.
            eprintln!("tack: cannot create the board window: {e}");
            std::process::exit(1);
        }
    }
    if let Some(hwnd) = board_hwnd(app) {
        BOARD.store(hwnd.0 as isize, Ordering::Release);
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
/// user data folder at [`webview_data_dir`].
fn create_window(app: &AppHandle) -> tauri::Result<()> {
    let Some(config) = app.config().app.windows.iter().find(|w| w.label == WINDOW_LABEL).cloned() else {
        return Err(tauri::Error::WindowNotFound);
    };
    let mut builder = WebviewWindowBuilder::from_config(app, &config)?;
    if let Some(dir) = webview_data_dir(app) {
        builder = builder.data_directory(dir);
    }
    builder.build()?;
    Ok(())
}

/// WebView2's user data folder, shared by all of Tack's windows:
/// `%LOCALAPPDATA%\Tack\WebView2`, next to Tack's other local data, rather
/// than a folder named after the bundle identifier.
pub fn webview_data_dir(app: &AppHandle) -> Option<std::path::PathBuf> {
    app.path().local_data_dir().ok().map(|local| local.join("Tack").join("WebView2"))
}

/// The window starts hidden, so its web view can sleep as soon as the UI has
/// loaded the board. Polls only until then.
///
/// Before it sleeps, the window is shown once, click-through, and the page
/// draws the board unseen (`board:warm-up`): the first show of a WebView2
/// window sets up its GPU surfaces and the first drawing of the board is
/// slow, with no frame reaching the screen meanwhile. Paid here, the first
/// reveal (or flight) is seen from its first frame.
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
            let place = Placement::under_pointer();
            overlay::show_at(hwnd, place.pos, place.size);
            events::warm_up(&handle);
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

/// Whether the board should let the pointer through, as last decided.
static IGNORING: AtomicBool = AtomicBool::new(true);

/// Applied on the poller's thread, not queued for the main thread: a drag
/// out runs inside a main-thread callback, so anything queued would land
/// after the drop, and the drop would hit the board instead of the app below.
pub fn set_click_through(ignore: bool) {
    trace!("click-through {ignore}");
    IGNORING.store(ignore, Ordering::Release);
    sync_click_through();
}

/// Brings the window in line with [`IGNORING`]. Showing the window makes it
/// click-through, and the poller may decide otherwise on another thread at
/// the same moment, so it applies until the decision holds still.
fn sync_click_through() {
    let raw = BOARD.load(Ordering::Acquire);
    if raw == 0 {
        return;
    }
    loop {
        let ignore = IGNORING.load(Ordering::Acquire);
        overlay::set_click_through(HWND(raw as *mut _), ignore);
        if IGNORING.load(Ordering::Acquire) == ignore {
            break;
        }
    }
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
    show(app, reason, Placement::under_pointer(), speed(app).peek());
}

/// Whether a new screenshot may bring the board down now, i.e. nothing full
/// screen holds it back (see [`fullscreen_holds_back`]). May wait up to
/// [`FULLSCREEN_GRACE`]; call it from a worker thread.
pub fn new_may_reveal(app: &AppHandle) -> bool {
    !fullscreen_holds_back(app)
}

/// Slides the board down at `place`, with no full-screen check. For a new
/// screenshot, `peek` is how long it stays down on its own (the speed's
/// `peek`, `flight_peek` or `notice_peek`). A board already down on another
/// monitor moves to `place`.
pub fn show(app: &AppHandle, reason: RevealReason, place: Placement, peek: Duration) {
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
        #[cfg(debug_assertions)]
        let scale = s.settings.reveal.motion_scale();
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
            // Never cut short a longer peek (the tip's) already running.
            RevealReason::New => {
                v.peek_until = v.peek_until.max(Some(now + peek));
                trace!("reveal {reason:?}: peek {peek:?} at scale {scale}");
            }
            RevealReason::Edge => {}
        }
        if v.shown {
            if reason == RevealReason::New && (v.window_pos != place.pos || v.window_size != place.size) {
                // Down on another monitor than the new capture's: it moves
                // there, as it is.
                trace!("reveal {reason:?}: already shown, moving to {:?}", place.pos);
                v.window_pos = place.pos;
                v.window_size = place.size;
                v.monitor_top = place.monitor_top;
                v.scale = place.scale;
                v.entered = false;
                let (generation, handle) = (v.generation, app.clone());
                drop(s);
                let _ = app.run_on_main_thread(move || {
                    if lock(&handle).view.generation != generation {
                        return;
                    }
                    if let Some(hwnd) = board_hwnd(&handle) {
                        overlay::show_at(hwnd, place.pos, place.size);
                        sync_click_through();
                        raise_settings_if_held(&handle);
                    }
                });
            } else {
                trace!("reveal {reason:?}: already shown");
            }
            return;
        }
        v.shown = true;
        v.generation += 1;
        v.reason = reason;
        v.entered = false;
        v.window_pos = place.pos;
        v.window_size = place.size;
        v.monitor_top = place.monitor_top;
        v.scale = place.scale;
        v.ignoring = true;
        IGNORING.store(true, Ordering::Release);
        (v.generation, v.window_pos, v.window_size)
    };
    // It may be idling (edge reveal off); the board needs it from now on.
    edge_reveal::wake();

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
            sync_click_through();
            raise_settings_if_held(&app);
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

/// Slides the board up, and hides the window once the slide is over (the
/// speed's `tuck_hide_delay`, read when the tuck starts).
///
/// Any tuck ends the Settings hold ([`hold_for_settings`]): it is the user's
/// own say (the shortcut, the tray, Esc) or a full-screen app's, and neither
/// is overruled by an open Settings window. The board stays up only until
/// the next reveal then; opening Settings again holds it again.
pub fn tuck(app: &AppHandle) {
    let (tucked, hide_after) = {
        let mut s = lock(app);
        let hide_after = s.settings.reveal.tuck_hide_delay();
        let Some(tucked) = s.view.tuck() else { return };
        (tucked, hide_after)
    };
    let generation = tucked.generation;
    events::tuck(app);
    trace!("tuck gen={generation}: board:tuck sent, the window hides in {hide_after:?}");
    if tucked.hold_ended {
        trace!("tuck gen={generation}: the Settings hold ended");
        lower_settings(app);
    }
    let handle = app.clone();
    std::thread::spawn(move || {
        std::thread::sleep(hide_after);
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

/// The Settings window opens (`on`) or closes: while it is open the board
/// stays down, so a change of colour, frame, paper or speed shows live, and
/// it tucks when Settings closes.
///
/// Opening reveals the board if it is not down, as the tray does (it never
/// takes the keyboard from Settings: the reason is [`RevealReason::Tray`],
/// and only the shortcut's reveal asks for focus), unless a full-screen app
/// is in front. A board already down is just kept: no new swing. While
/// held, the pointer poller does not tuck it (leaving, the peek ending, a
/// click in Settings), a full-screen app still does, and so does an explicit
/// tuck; both end the hold. The Settings window is raised above the board's
/// overlay for as long as it is held (on a short screen they overlap).
///
/// Call it from a worker thread (a reveal may be requested) or the main
/// thread (the window's destroyed event).
pub fn hold_for_settings(app: &AppHandle, on: bool) {
    if !on {
        let tuck_now = lock(app).view.release_settings();
        trace!("settings closed: hold released, tuck={tuck_now}");
        lower_settings(app);
        if tuck_now {
            tuck(app);
        }
        return;
    }
    let (needs_reveal, ready) = {
        let mut s = lock(app);
        (s.view.hold_for_settings(), s.ui_ready)
    };
    trace!("settings open: hold requested, reveal needed={needs_reveal}, ui_ready={ready}");
    if !ready {
        // The flag is kept: the board is held once it is revealed.
        return;
    }
    if !needs_reveal {
        let handle = app.clone();
        let _ = app.run_on_main_thread(move || raise_settings_if_held(&handle));
        return;
    }
    // Asked afresh, not the poller's last reading, which may be stale while
    // the board is tucked. Snipping Tool's own overlay does not count.
    if overlay::fullscreen_app_in_front() && !snipping_tool::in_front() {
        lock(app).view.settings_open = false;
        trace!("settings open: full screen in front, the board stays up");
        return;
    }
    show(app, RevealReason::Tray, Placement::under_pointer(), Duration::ZERO);
}

/// The Settings window's handle, if it is open.
fn settings_hwnd(app: &AppHandle) -> Option<HWND> {
    app.get_webview_window(settings_window::LABEL)?.hwnd().ok()
}

/// Puts Settings above the board's overlay (both are topmost, and showing
/// the board puts it on top), if Settings is holding the board. Main thread.
fn raise_settings_if_held(app: &AppHandle) {
    if !lock(app).view.settings_open {
        return;
    }
    if let Some(hwnd) = settings_hwnd(app) {
        overlay::set_topmost(hwnd, true);
    }
}

/// Settings back among the ordinary windows. Queued for the main thread,
/// which also runs after the window's destruction, when there is nothing
/// left to do.
fn lower_settings(app: &AppHandle) {
    let handle = app.clone();
    let _ = app.run_on_main_thread(move || {
        if let Some(hwnd) = settings_hwnd(&handle) {
            overlay::set_topmost(hwnd, false);
        }
    });
}

/// The one-time tip under the board: showing at `rect` (physical px,
/// relative to the window), or closed (`None`). Once it shows it never shows
/// again, and it keeps the board down a while so it can be read; once it is
/// closed the board goes back up as usual.
pub fn set_tip(app: &AppHandle, rect: Option<Rect>) {
    let mut s = lock(app);
    if !s.view.shown {
        return;
    }
    let now = Instant::now();
    match rect {
        Some(rect) => {
            s.view.tip = Some(rect);
            s.view.peek_until = s.view.peek_until.max(Some(now + TIP_PEEK));
            if !s.settings.snip_tip_shown {
                s.settings.snip_tip_shown = true;
                s.save();
            }
        }
        None => {
            s.view.tip = None;
            if s.view.peek_until.is_some() {
                s.view.peek_until = Some(now);
            }
        }
    }
}

/// The hotkey and the tray icon: down if it is up, up if it is down.
pub fn toggle(app: &AppHandle, reason: RevealReason) {
    if lock(app).view.shown {
        trace!("tuck requested: {reason:?} toggle");
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
        trace!("tuck requested: the pointer poller (peek over, pointer left, click outside or full screen)");
        tuck(&self.0);
    }

    fn set_click_through(&self, ignore: bool) {
        set_click_through(ignore);
    }

    fn pointer_left(&self) {
        events::pointer_left(&self.0);
    }
}
