//! Updates. Tack looks for a newer version on GitHub, tells the Settings
//! window and the tray, and installs it when the person says so. `plugin.rs`
//! is the one place that touches the network, through `tauri-plugin-updater`
//! and only from Rust; `status.rs` is what the UI is told; this file keeps
//! the state and decides when to check.
//!
//! The checker is one thread that parks until the next check is due, or
//! until a setting changes (it reads `settings.updates.check` each time it
//! wakes), so an idle Tack does nothing for it. The first check is 45 s after
//! start, then one a day. A check the person asks for (Settings, the tray)
//! goes ahead whatever the setting says. Every change of status is sent to
//! all windows as `updates:status`.
//!
//! `config_guard.rs` holds tests that pin the safeguards in `tauri.conf.json`
//! (one HTTPS endpoint, the built-in signing key, signed versions required,
//! no updater permission for the web views).

mod plugin;
mod status;

#[cfg(test)]
mod config_guard;

use std::sync::{Mutex, MutexGuard};
use std::thread;
use std::time::Duration;

use tack_core::history::now_ms;
use tauri::{AppHandle, Listener, Manager};
use tauri_plugin_updater::Update;

use crate::ipc::events;
use crate::state::lock;
use crate::tray;

pub use status::{Phase, UpdateStatus};

/// After Tack starts.
const FIRST_CHECK_DELAY: Duration = Duration::from_secs(45);
/// After a check starts, failed or not.
const CHECK_INTERVAL: Duration = Duration::from_secs(24 * 60 * 60);

struct Inner {
    status: UpdateStatus,
    /// What `Available` installs.
    update: Option<Update>,
    /// When the checker looks next, in ms since the Unix epoch.
    next_check: u64,
}

impl Inner {
    /// Starts a check, unless one or an install is going, or (for the
    /// checker) one is waiting to be installed. Either way the next turn is
    /// a day away.
    fn begin_check(&mut self, now: u64, by_checker: bool) -> bool {
        self.next_check = now + CHECK_INTERVAL.as_millis() as u64;
        let state = self.status.state;
        if state.is_busy() || (by_checker && state == Phase::Available) {
            return false;
        }
        self.update = None;
        self.status = self.status.checking();
        true
    }
}

/// What the app keeps.
struct Updates(Mutex<Inner>);

impl Updates {
    fn lock(&self) -> MutexGuard<'_, Inner> {
        // A panic in another thread must not take the updater down with it.
        self.0.lock().unwrap_or_else(|p| p.into_inner())
    }
}

/// Starts the checker. Once, at startup, after the tray exists.
pub fn start(app: &AppHandle) {
    let next_check = now_ms() + FIRST_CHECK_DELAY.as_millis() as u64;
    app.manage(Updates(Mutex::new(Inner { status: UpdateStatus::idle(), update: None, next_check })));

    let checker_app = app.clone();
    if let Ok(checker) = thread::Builder::new().name("tack-updates".into()).spawn(move || check_when_due(checker_app)) {
        // A setting changed (any of them): the check may have been switched on.
        let checker = checker.thread().clone();
        app.listen(events::SETTINGS_CHANGED, move |_| checker.unpark());
    }
}

/// The checker's loop: check when due, else park until then (for good while
/// the check is off). A wake, spurious or not, only looks again.
fn check_when_due(app: AppHandle) {
    loop {
        let enabled = lock(&app).settings.updates.check;
        let wait = wait_for(app.state::<Updates>().lock().next_check, now_ms());
        if !enabled {
            thread::park();
        } else if wait.is_zero() {
            run_check(&app, true);
        } else {
            thread::park_timeout(wait);
        }
    }
}

/// How long until `next_check`. At most a day: if the clock was set back
/// the check is not pushed out further than that.
fn wait_for(next_check: u64, now: u64) -> Duration {
    Duration::from_millis(next_check.saturating_sub(now)).min(CHECK_INTERVAL)
}

/// The status as it stands (idle before [`start`]).
pub fn status(app: &AppHandle) -> UpdateStatus {
    app.try_state::<Updates>().map_or_else(UpdateStatus::idle, |updates| updates.lock().status.clone())
}

/// Checks now, on the calling thread, which waits for the answer.
pub fn check(app: &AppHandle) -> UpdateStatus {
    run_check(app, false)
}

fn run_check(app: &AppHandle, by_checker: bool) -> UpdateStatus {
    let Some(updates) = app.try_state::<Updates>() else { return UpdateStatus::idle() };
    let checking = {
        let mut inner = updates.lock();
        if !inner.begin_check(now_ms(), by_checker) {
            return inner.status.clone();
        }
        inner.status.clone()
    };
    announce(app, &checking);

    let found = plugin::check(app);
    change(app, &updates, |inner| match found {
        Ok(Some(update)) => {
            inner.status = UpdateStatus::found(update.version.clone(), update.body.clone(), now_ms());
            inner.update = Some(update);
        }
        Ok(None) => inner.status = UpdateStatus::up_to_date(now_ms()),
        Err(message) => inner.status = inner.status.failed(message),
    })
}

/// Downloads, verifies and installs the version the last check found, on the
/// calling thread. When the installer starts Tack quits, and the installer
/// starts it again: that is the one case this does not return. After a
/// failure the update has to be found again.
pub fn install(app: &AppHandle) -> UpdateStatus {
    let Some(updates) = app.try_state::<Updates>() else { return UpdateStatus::idle() };
    let (update, downloading) = {
        let mut inner = updates.lock();
        if inner.status.state != Phase::Available {
            return inner.status.clone();
        }
        let Some(update) = inner.update.take() else { return inner.status.clone() };
        inner.status = UpdateStatus { state: Phase::Downloading, ..inner.status.clone() };
        (update, inner.status.clone())
    };
    announce(app, &downloading);

    let result = plugin::install(
        &update,
        |percent| {
            change(app, &updates, |inner| inner.status.progress = Some(percent as f64 / 100.0));
        },
        || {
            change(app, &updates, |inner| {
                inner.status = UpdateStatus { state: Phase::Installing, progress: Some(1.0), ..inner.status.clone() }
            });
        },
    );
    match result {
        Ok(()) => status(app),
        Err(message) => change(app, &updates, |inner| inner.status = inner.status.failed(message)),
    }
}

/// Changes the state, then tells everyone (off the lock).
fn change(app: &AppHandle, updates: &Updates, change: impl FnOnce(&mut Inner)) -> UpdateStatus {
    let status = {
        let mut inner = updates.lock();
        change(&mut inner);
        inner.status.clone()
    };
    announce(app, &status);
    status
}

fn announce(app: &AppHandle, status: &UpdateStatus) {
    events::updates_status(app, status);
    tray::set_update_label(app, &tray::update_label(status));
}

#[cfg(test)]
mod tests {
    use super::*;

    const DAY: u64 = 24 * 60 * 60 * 1000;

    fn inner(state: Phase) -> Inner {
        Inner { status: UpdateStatus { state, ..UpdateStatus::idle() }, update: None, next_check: 0 }
    }

    #[test]
    fn the_wait_is_until_the_due_time_but_never_more_than_a_day() {
        assert_eq!(wait_for(1_000 + 45_000, 1_000), Duration::from_secs(45));
        assert_eq!(wait_for(1_000, 1_000), Duration::ZERO);
        assert_eq!(wait_for(1_000, 9_000), Duration::ZERO);
        // The clock was set back a week.
        assert_eq!(wait_for(1_000 + 7 * DAY, 1_000), CHECK_INTERVAL);
    }

    #[test]
    fn any_check_pushes_the_next_one_a_day_out() {
        for (state, by_checker, starts) in [
            (Phase::Idle, true, true),
            (Phase::Error, true, true),
            (Phase::UpToDate, false, true),
            (Phase::Available, false, true),
            (Phase::Available, true, false),
            (Phase::Checking, false, false),
            (Phase::Downloading, true, false),
            (Phase::Installing, false, false),
        ] {
            let mut inner = inner(state);
            assert_eq!(inner.begin_check(500, by_checker), starts, "{state:?} {by_checker}");
            assert_eq!(inner.next_check, 500 + DAY);
            assert_eq!(inner.status.state, if starts { Phase::Checking } else { state });
        }
    }
}
