//! Tack's two global shortcuts, show-or-hide (`keyboard.rs`) and pin the
//! selection (`notes.rs`): registering them, what a press does, and the
//! Shortcuts dialog that changes them (`ui/shortcuts.html`, created only
//! while open). While the dialog listens for a new chord the shortcuts are
//! paused, so pressing Tack's own chord there reaches the page.

use std::sync::Mutex;

use serde::Serialize;
use tack_core::shortcut::{self, Chord, DEFAULT_PIN, DEFAULT_TOGGLE};
use tack_core::RevealReason;
use tack_windows::hotkey::{self, Action, Hotkeys, Status};
use tack_windows::selection;
use tauri::{AppHandle, Manager, WindowEvent};

use crate::state::lock;
use crate::{dialogs, notes, reveal, tray};

/// The dialog window's label.
pub const DIALOG_LABEL: &str = "shortcuts";

/// The hotkey thread and how the last registration went.
struct Registered {
    hotkeys: Option<Hotkeys>,
    statuses: [Status; 2],
    /// The dialog has paused the shortcuts.
    paused: bool,
}

static REGISTERED: Mutex<Option<Registered>> = Mutex::new(None);

fn registered() -> std::sync::MutexGuard<'static, Option<Registered>> {
    REGISTERED.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Starts the hotkey thread and registers the saved shortcuts. A shortcut
/// another app already has is reported in the tray menu.
pub fn start(app: &AppHandle) {
    let handle = app.clone();
    let hotkeys = hotkey::start(move |action| on_press(&handle, action));
    let (toggle, pin) = {
        let s = lock(app);
        (s.settings.toggle_chord(), s.settings.pin_chord())
    };
    let statuses = match &hotkeys {
        Some(hotkeys) => hotkeys.set(toggle, pin),
        None => [Status::InUse, Status::InUse],
    };
    *registered() = Some(Registered { hotkeys, statuses, paused: false });
}

/// A shortcut was pressed (on the hotkey thread: hand the work on).
fn on_press(app: &AppHandle, action: Action) {
    match action {
        Action::Toggle => reveal::toggle(app, RevealReason::Hotkey),
        Action::PinSelection => {
            // Now, while Win and Alt are still down and before the user can
            // switch to another window: the keys go to this one only.
            let target = selection::Target::in_front();
            selection::mask_menu();
            let app = app.clone();
            let spawned = std::thread::Builder::new().name("tack-pin-selection".into()).spawn(move || {
                let _line = notes::pin_selection(&app, target);
                trace!("{_line}");
            });
            if let Err(e) = spawned {
                eprintln!("tack: cannot pin the selection: {e}");
            }
        }
    }
}

/// One shortcut as the dialog shows it.
#[derive(Serialize, Clone, Debug, PartialEq)]
pub struct Shortcut {
    /// "Win+Alt+S", or "" when off.
    chord: String,
    /// "ok", "off", "in-use" (another app has it) or "invalid".
    status: &'static str,
}

#[derive(Serialize)]
pub struct Defaults {
    toggle: &'static str,
    pin: &'static str,
}

/// Both shortcuts.
#[derive(Serialize)]
pub struct Pair {
    toggle: Shortcut,
    pin: Shortcut,
}

/// What the dialog shows when it opens.
#[derive(Serialize)]
pub struct DialogState {
    toggle: Shortcut,
    pin: Shortcut,
    defaults: Defaults,
}

fn status_name(status: &Status) -> &'static str {
    match status {
        Status::Ok => "ok",
        Status::Off => "off",
        Status::InUse => "in-use",
    }
}

/// The saved shortcuts and how registering them went.
pub fn current(app: &AppHandle) -> Pair {
    let (toggle, pin) = {
        let s = lock(app);
        (shown(s.settings.toggle_chord()), shown(s.settings.pin_chord()))
    };
    let statuses = registered().as_ref().map(|r| r.statuses.clone()).unwrap_or([Status::Off, Status::Off]);
    Pair {
        toggle: Shortcut { chord: toggle, status: status_name(&statuses[0]) },
        pin: Shortcut { chord: pin, status: status_name(&statuses[1]) },
    }
}

fn shown(chord: Option<Chord>) -> String {
    chord.map(|c| c.to_string()).unwrap_or_default()
}

pub fn dialog_state(app: &AppHandle) -> DialogState {
    let Pair { toggle, pin } = current(app);
    DialogState { toggle, pin, defaults: Defaults { toggle: DEFAULT_TOGGLE, pin: DEFAULT_PIN } }
}

/// The shortcuts that could not be registered, as "Win+Alt+C", for the tray.
pub fn in_use(app: &AppHandle) -> Vec<String> {
    let Pair { toggle, pin } = current(app);
    [toggle, pin].into_iter().filter(|s| s.status == "in-use").map(|s| s.chord).collect()
}

/// The toggle shortcut as the tray shows it next to "Show board".
pub fn toggle_label(app: &AppHandle) -> String {
    shown(lock(app).settings.toggle_chord())
}

/// Tries `toggle` and `pin` (as "Win+Alt+S"; "" turns one off). If both
/// work they are saved; if either is invalid or taken, nothing changes and
/// the old shortcuts stay registered. Returns how each requested one fared.
pub fn set(app: &AppHandle, toggle: &str, pin: &str) -> Pair {
    let parsed = [Chord::parse(toggle), Chord::parse(pin)];
    let mut result = Pair {
        toggle: Shortcut { chord: shortcut::tidy(toggle), status: "ok" },
        pin: Shortcut { chord: shortcut::tidy(pin), status: "ok" },
    };
    let [Ok(toggle_chord), Ok(pin_chord)] = parsed.clone() else {
        if parsed[0].is_err() {
            result.toggle.status = "invalid";
        }
        if parsed[1].is_err() {
            result.pin.status = "invalid";
        }
        return result;
    };
    if toggle_chord.is_some() && toggle_chord == pin_chord {
        // Both on one chord: the second is taken, by Tack itself.
        result.pin.status = "in-use";
        return result;
    }
    let (old_toggle, old_pin) = {
        let s = lock(app);
        (s.settings.toggle_chord(), s.settings.pin_chord())
    };
    let mut guard = registered();
    let Some(reg) = guard.as_mut() else { return result };
    let Some(hotkeys) = reg.hotkeys.as_ref() else { return result };
    let statuses = hotkeys.set(toggle_chord, pin_chord);
    result.toggle.status = status_name(&statuses[0]);
    result.pin.status = status_name(&statuses[1]);
    if statuses.contains(&Status::InUse) {
        // Put the old ones back.
        reg.statuses = hotkeys.set(old_toggle, old_pin);
        if reg.paused {
            reg.statuses = hotkeys.pause(true);
        }
        return result;
    }
    reg.statuses = statuses;
    if reg.paused {
        reg.statuses = hotkeys.pause(true);
    }
    drop(guard);
    {
        let mut s = lock(app);
        s.settings.toggle_shortcut = result.toggle.chord.clone();
        s.settings.pin_shortcut = result.pin.chord.clone();
        s.save();
    }
    tray::refresh(app);
    result
}

/// The dialog listens for a new chord (true) or is done listening.
pub fn pause(paused: bool) {
    let mut guard = registered();
    let Some(reg) = guard.as_mut() else { return };
    if reg.paused == paused {
        return;
    }
    reg.paused = paused;
    if let Some(hotkeys) = reg.hotkeys.as_ref() {
        let statuses = hotkeys.pause(paused);
        if !paused {
            reg.statuses = statuses;
        }
    }
}

/// Opens the Shortcuts dialog, or brings it to the front. Never on the main
/// thread: building a window waits for the event loop.
pub fn open_dialog(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(DIALOG_LABEL) {
        let _ = window.unminimize();
        let _ = window.set_focus();
        return;
    }
    match dialogs::open(app, DIALOG_LABEL, "shortcuts.html", "Tack shortcuts", (460.0, 320.0)) {
        Ok(window) => {
            window.on_window_event(|event| {
                if let WindowEvent::Destroyed = event {
                    // Closed while listening: the shortcuts come back.
                    pause(false);
                }
            });
        }
        Err(e) => eprintln!("tack: cannot open the Shortcuts dialog: {e}"),
    }
}

/// Closes the Shortcuts dialog.
pub fn close_dialog(app: &AppHandle) {
    pause(false);
    if let Some(window) = app.get_webview_window(DIALOG_LABEL) {
        let _ = window.close();
    }
}
