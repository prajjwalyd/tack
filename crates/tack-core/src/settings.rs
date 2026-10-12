//! The user's settings, changed in the Settings window or from the tray, and
//! saved in board.json.

use serde::{Deserialize, Serialize};

use crate::felt::Felt;
use crate::frame::Frame;
use crate::paper::NotePaper;
use crate::phone::PhoneSettings;
use crate::reveal_speed::RevealSpeed;
use crate::shortcut::{Chord, DEFAULT_PIN, DEFAULT_TOGGLE};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    /// The pin and pop sounds.
    pub sound: bool,
    /// Resting the pointer against the top edge reveals the board.
    pub edge_reveal: bool,
    /// How soon resting at the top edge reveals the board, and how fast it
    /// moves.
    pub reveal: RevealSpeed,
    /// How the board looks.
    pub appearance: Appearance,
    /// Whether Tack looks for new versions.
    pub updates: UpdateSettings,
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
            reveal: RevealSpeed::default(),
            appearance: Appearance::default(),
            updates: UpdateSettings::default(),
            snip_tip_shown: false,
            toggle_shortcut: DEFAULT_TOGGLE.into(),
            pin_shortcut: DEFAULT_PIN.into(),
            phone: PhoneSettings::default(),
        }
    }
}

/// How the board looks.
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Appearance {
    /// The board's colour.
    pub felt: Felt,
    /// The finish of the metal frame around it.
    pub frame: Frame,
    /// The colour of the paper notes.
    pub paper: NotePaper,
}

/// Looking for new versions.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct UpdateSettings {
    /// Look for a new version once when Tack starts (and say so when there is one).
    pub check: bool,
}

impl Default for UpdateSettings {
    fn default() -> Self {
        UpdateSettings { check: true }
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

fn chord_or(text: &str, default: &str) -> Option<Chord> {
    Chord::parse(text).unwrap_or_else(|_| Chord::parse(default).ok().flatten())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_new_settings_default_to_the_way_it_always_was() {
        let settings = Settings::default();
        assert_eq!(settings.reveal, RevealSpeed::Normal);
        assert_eq!(settings.appearance.felt, Felt::default());
        assert_eq!(settings.appearance.frame.id(), "aluminium");
        assert_eq!(settings.appearance.paper.id(), "white");
        assert!(settings.updates.check);
    }

    #[test]
    fn they_are_saved_as_camel_case_json() {
        let json = serde_json::to_value(Settings::default()).unwrap();
        assert_eq!(json["reveal"], "normal");
        assert_eq!(json["appearance"], serde_json::json!({ "felt": "green", "frame": "aluminium", "paper": "white" }));
        assert_eq!(json["updates"], serde_json::json!({ "check": true }));
    }

    #[test]
    fn they_survive_a_round_trip() {
        let settings = Settings {
            reveal: RevealSpeed::Instant,
            appearance: Appearance {
                felt: Felt::parse("#A1B2C3"),
                frame: Frame::parse("brass"),
                paper: NotePaper::parse("mint"),
            },
            updates: UpdateSettings { check: false },
            ..Settings::default()
        };
        let json = serde_json::to_string(&settings).unwrap();
        assert!(json.contains(r##""appearance":{"felt":"#a1b2c3","frame":"brass","paper":"mint"}"##), "{json}");
        assert_eq!(serde_json::from_str::<Settings>(&json).unwrap(), settings);
    }

    #[test]
    fn a_file_from_before_them_still_loads_with_the_defaults() {
        let old = r#"{ "sound": false, "edgeReveal": false, "toggleShortcut": "Win+Alt+Q" }"#;
        let settings: Settings = serde_json::from_str(old).unwrap();
        assert!(!settings.sound && !settings.edge_reveal);
        assert_eq!(settings.toggle_shortcut, "Win+Alt+Q");
        assert_eq!(settings.reveal, RevealSpeed::Normal);
        assert_eq!(settings.appearance, Appearance::default());
        assert!(settings.updates.check);
        // Or with only part of a group.
        let partial: Settings = serde_json::from_str(r#"{ "appearance": {}, "updates": {} }"#).unwrap();
        assert_eq!(partial, Settings::default());
    }

    #[test]
    fn bad_values_fall_back_and_never_stop_the_file_loading() {
        let bad = r##"{ "sound": false, "reveal": "turbo", "appearance": { "felt": "#12", "frame": "gold", "paper": 3 }, "updates": { "check": false } }"##;
        let settings: Settings = serde_json::from_str(bad).unwrap();
        assert_eq!(settings.reveal, RevealSpeed::Normal);
        assert_eq!(settings.appearance, Appearance::default());
        assert!(!settings.sound && !settings.updates.check, "the good values stay");
        let wrong_types = r#"{ "reveal": 3, "appearance": { "felt": null, "frame": [], "paper": {} } }"#;
        let settings: Settings = serde_json::from_str(wrong_types).unwrap();
        assert_eq!((settings.reveal, settings.appearance), (RevealSpeed::Normal, Appearance::default()));
    }

    #[test]
    fn each_appearance_setting_falls_back_on_its_own() {
        let one_bad = r##"{ "appearance": { "felt": "navy", "frame": "gold", "paper": "blush" } }"##;
        let appearance = serde_json::from_str::<Settings>(one_bad).unwrap().appearance;
        assert_eq!(appearance.felt.to_string(), "navy");
        assert_eq!(appearance.frame, Frame::default());
        assert_eq!(appearance.paper.id(), "blush");
    }

    #[test]
    fn a_file_from_before_the_frame_and_paper_keeps_its_felt() {
        let old = r##"{ "appearance": { "felt": "plum" } }"##;
        let appearance = serde_json::from_str::<Settings>(old).unwrap().appearance;
        assert_eq!(appearance.felt.to_string(), "plum");
        assert_eq!((appearance.frame, appearance.paper), (Frame::default(), NotePaper::default()));
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
