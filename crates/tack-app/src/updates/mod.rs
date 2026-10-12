//! Updates. Tack looks for a newer version on GitHub, tells the Settings
//! window and the tray, and installs it when the person says so. `plugin.rs`
//! is the one place that touches the network, through `tauri-plugin-updater`
//! and only from Rust; `status.rs` is what the UI is told; this file keeps
//! the state and decides when to check.
//!
//! Tack checks once on its own, about 45 s after it starts, if
//! `settings.updates.check` is on and nothing has been checked since. A check
//! the person asks for (Settings, the tray) goes ahead whatever the setting
//! says. Every change of status is sent to all windows as `updates:status`.
//!
//! `config_guard.rs` holds tests that pin the safeguards in `tauri.conf.json`
//! (one HTTPS endpoint, the built-in signing key, signed versions required,
//! no updater permission for the web views).

mod plugin;
mod status;

#[cfg(test)]
mod config_guard;

use std::sync::{Mutex, MutexGuard};
use std::time::Duration;

use tack_core::history::now_ms;
use tauri::{AppHandle, Manager};
use tauri_plugin_updater::Update;

use crate::ipc::events;
use crate::reveal::after;
use crate::state::lock;
use crate::tray;

pub use status::{Phase, UpdateStatus};

/// After Tack starts, when its one automatic check is made.
const START_CHECK_DELAY: Duration = Duration::from_secs(45);

struct Inner {
    status: UpdateStatus,
    /// What `Available` installs.
    update: Option<Update>,
}

impl Inner {
    /// Starts a check, unless one or an install is going. The automatic one
    /// also needs the setting on and nothing checked yet.
    fn begin_check(&mut self, automatic: bool, setting_on: bool) -> bool {
        let state = self.status.state;
        let allowed = if automatic { setting_on && state == Phase::Idle } else { !state.is_busy() };
        if allowed {
            self.update = None;
            self.status = self.status.checking();
        }
        allowed
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

/// Sets up the state and makes the one automatic check after a delay (the
/// setting is read then, so switching it off meanwhile counts). Once, at
/// startup, after the tray exists.
pub fn start(app: &AppHandle) {
    app.manage(Updates(Mutex::new(Inner { status: UpdateStatus::idle(), update: None })));
    let app = app.clone();
    after(START_CHECK_DELAY, move || {
        run_check(&app, true);
    });
}

/// The status as it stands (idle before [`start`]).
pub fn status(app: &AppHandle) -> UpdateStatus {
    app.try_state::<Updates>().map_or_else(UpdateStatus::idle, |updates| updates.lock().status.clone())
}

/// Checks now, on the calling thread, which waits for the answer.
pub fn check(app: &AppHandle) -> UpdateStatus {
    run_check(app, false)
}

fn run_check(app: &AppHandle, automatic: bool) -> UpdateStatus {
    let Some(updates) = app.try_state::<Updates>() else { return UpdateStatus::idle() };
    let setting_on = lock(app).settings.updates.check;
    let checking = {
        let mut inner = updates.lock();
        if !inner.begin_check(automatic, setting_on) {
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

    fn inner(state: Phase) -> Inner {
        Inner { status: UpdateStatus { state, ..UpdateStatus::idle() }, update: None }
    }

    #[test]
    fn a_check_starts_unless_one_or_an_install_is_going() {
        for (state, automatic, starts) in [
            (Phase::Idle, false, true),
            (Phase::Error, false, true),
            (Phase::UpToDate, false, true),
            (Phase::Available, false, true),
            (Phase::Checking, false, false),
            (Phase::Downloading, false, false),
            (Phase::Installing, false, false),
            (Phase::Idle, true, true),
            // The automatic one runs only while nothing has been checked.
            (Phase::Error, true, false),
            (Phase::UpToDate, true, false),
            (Phase::Available, true, false),
            (Phase::Checking, true, false),
            (Phase::Downloading, true, false),
        ] {
            let mut inner = inner(state);
            assert_eq!(inner.begin_check(automatic, true), starts, "{state:?} {automatic}");
            assert_eq!(inner.status.state, if starts { Phase::Checking } else { state });
        }
    }

    #[test]
    fn the_automatic_check_is_skipped_when_the_setting_is_off() {
        let mut inner = inner(Phase::Idle);
        assert!(!inner.begin_check(true, false));
        assert_eq!(inner.status.state, Phase::Idle);
        // The person's own check ignores the setting.
        assert!(inner.begin_check(false, false));
        assert_eq!(inner.status.state, Phase::Checking);
    }

    #[test]
    fn a_new_check_drops_the_version_the_last_one_found() {
        let mut inner = inner(Phase::Idle);
        inner.status = UpdateStatus::found("0.2.0".into(), None, 5);
        assert!(inner.begin_check(false, true));
        assert_eq!((inner.status.version, inner.status.checked_at), (None, Some(5)));
    }
}
