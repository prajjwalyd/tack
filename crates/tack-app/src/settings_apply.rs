//! Changing a setting, in one place. The Settings window's commands and the
//! tray's menu both end up here, so they never disagree: each change is
//! saved, shown in the tray's check marks, applied to whatever acts on it,
//! and announced to every window as `settings:changed` with the new
//! [`SettingsView`].
//!
//! An invalid value (a speed or colour that is not one) changes nothing.

use serde::Serialize;
use tack_core::felt::{self, FeltPreset};
use tack_core::{Felt, RevealSpeed, Settings};
use tack_windows::{autostart, edge_reveal};
use tauri::AppHandle;

use crate::ipc::events;
use crate::state::lock;
use crate::tray;

/// Every setting as the UI shows it.
#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct SettingsView {
    /// The pin and pop sounds.
    sound: bool,
    /// Resting the pointer against the top edge reveals the board.
    edge_reveal: bool,
    /// Tack starts with Windows (read from the Run key, not from board.json).
    autostart: bool,
    /// "instant", "fast" or "normal".
    reveal: RevealSpeed,
    /// The board colour: a preset's id, "accent" or "#rrggbb".
    felt: Felt,
    /// The colours to choose from, in order.
    felt_presets: &'static [FeltPreset],
    /// What the reveal animation's duration is multiplied by.
    motion_scale: f64,
    /// Tack looks for new versions.
    updates_check: bool,
    /// This version of Tack, as "0.2.0".
    version: String,
}

/// The settings as they stand now.
pub fn view(app: &AppHandle) -> SettingsView {
    let settings = lock(app).settings.clone();
    SettingsView {
        sound: settings.sound,
        edge_reveal: settings.edge_reveal,
        autostart: autostart::enabled(),
        reveal: settings.reveal,
        felt: settings.appearance.felt,
        felt_presets: &felt::PRESETS,
        motion_scale: settings.reveal.motion_scale(),
        updates_check: settings.updates.check,
        version: app.package_info().version.to_string(),
    }
}

/// Applies `change` to the settings and saves them if it changed any, then
/// brings everything else along: the tray's check marks and every window.
fn change(app: &AppHandle, change: impl FnOnce(&mut Settings)) -> SettingsView {
    {
        let mut s = lock(app);
        let before = s.settings.clone();
        change(&mut s.settings);
        if s.settings != before {
            s.save();
        }
    }
    announce(app)
}

/// Syncs the tray and tells the windows. Off the state lock: the tray's
/// items are changed on the UI thread, which may be waiting for it.
fn announce(app: &AppHandle) -> SettingsView {
    tray::sync_checks(app);
    let view = view(app);
    events::settings_changed(app, &view);
    view
}

pub fn set_sound(app: &AppHandle, on: bool) -> SettingsView {
    change(app, |s| s.sound = on)
}

pub fn set_edge_reveal(app: &AppHandle, on: bool) -> SettingsView {
    let view = change(app, |s| s.edge_reveal = on);
    // The poller idles while edge reveal is off; it looks again at once.
    edge_reveal::wake();
    view
}

/// Adds or removes the Run entry.
pub fn set_autostart(app: &AppHandle, on: bool) -> SettingsView {
    autostart::set(on);
    announce(app)
}

/// "instant", "fast" or "normal"; anything else changes nothing.
pub fn set_reveal(app: &AppHandle, speed: &str) -> SettingsView {
    match RevealSpeed::parse(speed) {
        Some(speed) => change(app, |s| s.reveal = speed),
        None => view(app),
    }
}

/// A preset's id, "accent" or "#rrggbb"; anything else changes nothing.
pub fn set_felt(app: &AppHandle, value: &str) -> SettingsView {
    match Felt::try_parse(value) {
        Some(felt) => change(app, |s| s.appearance.felt = felt),
        None => view(app),
    }
}

pub fn set_updates_check(app: &AppHandle, on: bool) -> SettingsView {
    change(app, |s| s.updates.check = on)
}
