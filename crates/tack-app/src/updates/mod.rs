//! Updates. Tack looks for a newer version on GitHub, tells the Settings
//! window and the tray, and installs it when the person says so. The state
//! machine (`machine.rs`) and the schedule (`schedule.rs`) are plain code
//! with tests; `plugin.rs` is the one place that touches the network, through
//! `tauri-plugin-updater` and only from Rust; this file ties them to the app.
//!
//! The checker is one thread that sleeps until the next check is due (or the
//! settings change), so an idle Tack does nothing for it. It reads
//! `settings.updates.check` each time it wakes; a check the person asks for
//! (Settings, the tray) goes ahead whatever the setting says. Every change
//! of status is sent to all windows as `updates:status`.
//!
//! `config_guard.rs` holds tests that pin the safeguards in `tauri.conf.json`
//! (one HTTPS endpoint, the built-in signing key, signed versions required,
//! no updater permission for the web views).

mod machine;
mod plugin;
mod schedule;
mod status;

#[cfg(test)]
mod config_guard;

use std::sync::{Arc, Condvar, Mutex};
use std::time::Duration;

use tack_core::history;
use tauri::{AppHandle, Listener, Manager};

use crate::ipc::events;
use crate::state::lock;
use crate::tray;

use machine::Updater;
use plugin::PluginSource;
pub use status::{Phase, UpdateStatus};

/// The updater and what wakes its thread: what the app keeps.
struct Handle {
    updater: Updater<PluginSource>,
    parker: Parker,
}

/// Starts the checker. Once, at startup, after the tray exists.
pub fn start(app: &AppHandle) {
    let announce = {
        let app = app.clone();
        // The tray's item is only touched when its text changes, not on every
        // step of a download.
        let shown = Mutex::new(String::new());
        move |status: &UpdateStatus| {
            events::updates_status(&app, status);
            let label = tray::update_label(status);
            let mut shown = shown.lock().unwrap_or_else(|p| p.into_inner());
            if *shown != label {
                tray::set_update_label(&app, &label);
                *shown = label;
            }
        }
    };
    let handle = Arc::new(Handle {
        updater: Updater::new(PluginSource::new(app.clone()), announce, history::now_ms),
        parker: Parker::default(),
    });
    app.manage(handle.clone());

    // A setting changed (any of them: this only looks again): the checker
    // may be asleep because the check was off.
    let on_settings = handle.clone();
    app.listen(events::SETTINGS_CHANGED, move |_| on_settings.parker.wake());

    let app = app.clone();
    let started = history::now_ms();
    let _ = std::thread::Builder::new().name("tack-updates".into()).spawn(move || check_when_due(app, handle, started));
}

/// The checker's loop: ask the schedule, then check or sleep.
fn check_when_due(app: AppHandle, handle: Arc<Handle>, started_ms: u64) {
    loop {
        let (phase, last_attempt_ms) = handle.updater.schedule_state();
        let inputs = schedule::Inputs {
            enabled: lock(&app).settings.updates.check,
            phase,
            now_ms: history::now_ms(),
            started_ms,
            last_attempt_ms,
        };
        match schedule::next_step(&inputs) {
            schedule::Step::CheckNow => {
                handle.updater.check();
            }
            schedule::Step::Sleep(wait) => handle.parker.park(wait),
        }
    }
}

/// The status as it stands (idle before [`start`]).
pub fn status(app: &AppHandle) -> UpdateStatus {
    match app.try_state::<Arc<Handle>>() {
        Some(handle) => handle.updater.status(),
        None => UpdateStatus::idle(),
    }
}

/// Checks now, on the calling thread, which waits for the answer.
pub fn check(app: &AppHandle) -> UpdateStatus {
    match app.try_state::<Arc<Handle>>() {
        Some(handle) => handle.updater.check(),
        None => UpdateStatus::idle(),
    }
}

/// Downloads, verifies and installs the version the last check found, on the
/// calling thread. When the installer starts Tack quits, and the installer
/// starts it again: that is the one case this does not return.
pub fn install(app: &AppHandle) -> UpdateStatus {
    match app.try_state::<Arc<Handle>>() {
        Some(handle) => handle.updater.install(),
        None => UpdateStatus::idle(),
    }
}

/// A flag with a timeout: the checker's sleep, cut short when it is woken.
#[derive(Default)]
struct Parker {
    woken: Mutex<bool>,
    signal: Condvar,
}

impl Parker {
    /// Returns when woken, or after `wait`; a wake that came first is used up.
    fn park(&self, wait: Duration) {
        let mut woken = self.woken.lock().unwrap_or_else(|p| p.into_inner());
        if !*woken {
            woken = self.signal.wait_timeout(woken, wait).unwrap_or_else(|p| p.into_inner()).0;
        }
        *woken = false;
    }

    fn wake(&self) {
        *self.woken.lock().unwrap_or_else(|p| p.into_inner()) = true;
        self.signal.notify_one();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Instant;

    #[test]
    fn a_wake_before_the_sleep_ends_it_at_once_and_is_used_up() {
        let parker = Parker::default();
        parker.wake();
        let start = Instant::now();
        parker.park(Duration::from_secs(30));
        assert!(start.elapsed() < Duration::from_secs(5));
        // Used up: the next sleep lasts.
        let start = Instant::now();
        parker.park(Duration::from_millis(60));
        assert!(start.elapsed() >= Duration::from_millis(50));
    }

    #[test]
    fn a_wake_from_another_thread_ends_the_sleep() {
        let parker = Arc::new(Parker::default());
        let waker = parker.clone();
        let thread = std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(50));
            waker.wake();
        });
        let start = Instant::now();
        parker.park(Duration::from_secs(30));
        assert!(start.elapsed() < Duration::from_secs(5));
        thread.join().unwrap();
    }
}
