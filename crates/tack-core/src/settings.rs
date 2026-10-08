//! The user's settings, toggled from the tray and saved in board.json.

use serde::{Deserialize, Serialize};

use crate::phone::PhoneSettings;
use crate::shortcut::{Chord, DEFAULT_PIN, DEFAULT_TOGGLE};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    /// The pin and pop sounds.
    pub sound: bool,
    /// Resting the pointer against the top edge reveals the board.
    pub edge_reveal: bool,
    /// The one-time tip about Snipping Tool's own notification (redundant
    /// once Tack shows each snip) has been shown.
    pub snip_tip_shown: bool,
    /// Shows or hides the board, as "Win+Alt+S" ("" for none). See
    /// [`crate::shortcut`].
    pub toggle_shortcut: String,
    /// Pins the current selection, as "Win+Alt+C" ("" for none).
    pub pin_shortcut: String,
    /// The board on the user's phone: the switch and the allowed devices.
    pub phone: PhoneSettings,
}

impl Default for Settings {
    fn default() -> Self {
        Settings {
            sound: true,
            edge_reveal: true,
            snip_tip_shown: false,
            toggle_shortcut: DEFAULT_TOGGLE.into(),
            pin_shortcut: DEFAULT_PIN.into(),
            phone: PhoneSettings::default(),
        }
    }
}

impl Settings {
    /// The toggle shortcut; a saved one that no longer parses falls back to
    /// the default.
    pub fn toggle_chord(&self) -> Option<Chord> {
        chord_or(&self.toggle_shortcut, DEFAULT_TOGGLE)
    }

    /// The pin-selection shortcut, with the same fallback.
    pub fn pin_chord(&self) -> Option<Chord> {
        chord_or(&self.pin_shortcut, DEFAULT_PIN)
    }
}

/// Former default show-or-hide shortcuts. Builds that shipped them saved the
/// default into board.json, so a saved one was Tack's choice, not the
/// user's. Win+Alt+T belongs to the Xbox Game Bar and never registers.
const RETIRED_TOGGLE_DEFAULTS: &[&str] = &["Win+Alt+T", "Ctrl+Alt+T"];

impl Settings {
    /// Moves a toggle shortcut still set to a retired default over to the
    /// current one. Called once when board.json is read.
    pub(crate) fn retire_old_defaults(&mut self) {
        let saved = Chord::parse(&self.toggle_shortcut).ok().flatten().map(|c| c.to_string());
        if saved.is_some_and(|s| RETIRED_TOGGLE_DEFAULTS.contains(&s.as_str())) {
            self.toggle_shortcut = DEFAULT_TOGGLE.into();
        }
    }
}

fn chord_or(text: &str, default: &str) -> Option<Chord> {
    Chord::parse(text).unwrap_or_else(|_| Chord::parse(default).ok().flatten())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_retired_default_moves_to_the_current_one() {
        for old in ["Win+Alt+T", "alt + win + t", "Ctrl+Alt+T"] {
            let mut settings = Settings { toggle_shortcut: old.into(), ..Settings::default() };
            settings.retire_old_defaults();
            assert_eq!(settings.toggle_shortcut, DEFAULT_TOGGLE, "{old}");
        }
        for chosen in ["Win+Alt+Q", "Ctrl+Shift+F9", ""] {
            let mut settings = Settings { toggle_shortcut: chosen.into(), ..Settings::default() };
            settings.retire_old_defaults();
            assert_eq!(settings.toggle_shortcut, chosen, "a shortcut the user chose stays");
        }
    }

    #[test]
    fn shortcuts_default_to_win_alt_and_survive_a_bad_value() {
        let settings = Settings::default();
        assert_eq!(settings.toggle_chord().unwrap().to_string(), "Win+Alt+S");
        assert_eq!(settings.pin_chord().unwrap().to_string(), "Win+Alt+C");
        let odd = Settings { toggle_shortcut: "Shift+T".into(), pin_shortcut: String::new(), ..Settings::default() };
        assert_eq!(odd.toggle_chord().unwrap().to_string(), "Win+Alt+S");
        assert_eq!(odd.pin_chord(), None, "an empty shortcut is off");
    }
}
