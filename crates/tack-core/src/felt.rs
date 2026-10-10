//! The board's colour: the user's "Board colour" setting. A preset, the
//! Windows accent colour (which the UI resolves itself, with the CSS
//! `AccentColor` keyword), or a custom `#rrggbb`.
//!
//! It is saved and sent to the UI as one plain string: a preset's id,
//! "accent", or "#rrggbb". The presets are listed here and nowhere else; the
//! UI gets them from the backend.

use std::fmt;

use serde::de::IgnoredAny;
use serde::{Deserialize, Deserializer, Serialize, Serializer};

/// A colour the user can pick from the list.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
pub struct FeltPreset {
    /// What is saved: lowercase letters.
    pub id: &'static str,
    /// What the list shows.
    pub name: &'static str,
    /// The felt in a light theme, "#rrggbb"; the UI darkens it for a dark one.
    pub hex: &'static str,
}

/// Every preset, in the order the list shows them. The first is the default.
pub const PRESETS: [FeltPreset; 7] = [
    FeltPreset { id: "green", name: "Green", hex: "#24503f" },
    FeltPreset { id: "navy", name: "Navy", hex: "#233a5e" },
    FeltPreset { id: "charcoal", name: "Charcoal", hex: "#3b3a3e" },
    FeltPreset { id: "burgundy", name: "Burgundy", hex: "#682b3b" },
    FeltPreset { id: "plum", name: "Plum", hex: "#4a2f5c" },
    FeltPreset { id: "teal", name: "Teal", hex: "#1f5560" },
    FeltPreset { id: "slate", name: "Slate", hex: "#42586e" },
];

/// The id that follows the Windows accent colour.
pub const ACCENT: &str = "accent";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Felt {
    Preset(&'static FeltPreset),
    /// Whatever accent colour Windows has.
    Accent,
    /// The user's own: red, green, blue.
    Custom([u8; 3]),
}

impl Default for Felt {
    fn default() -> Felt {
        Felt::Preset(&PRESETS[0])
    }
}

impl Felt {
    /// A preset's id exactly, "accent", or "#rrggbb" (any case); anything
    /// else is `None`.
    pub fn try_parse(text: &str) -> Option<Felt> {
        if text == ACCENT {
            return Some(Felt::Accent);
        }
        if let Some(preset) = PRESETS.iter().find(|p| p.id == text) {
            return Some(Felt::Preset(preset));
        }
        // Checked as bytes: slicing a stray multi-byte character would panic.
        let digits = text.strip_prefix('#')?.as_bytes();
        if digits.len() != 6 || !digits.iter().all(u8::is_ascii_hexdigit) {
            return None;
        }
        let channel = |i: usize| (hex_value(digits[i]) << 4) | hex_value(digits[i + 1]);
        Some(Felt::Custom([channel(0), channel(2), channel(4)]))
    }

    /// As [`Felt::try_parse`]; anything unknown or malformed is the default.
    pub fn parse(text: &str) -> Felt {
        Felt::try_parse(text).unwrap_or_default()
    }
}

/// The value of one hex digit; only called with one.
fn hex_value(digit: u8) -> u8 {
    match digit {
        b'0'..=b'9' => digit - b'0',
        b'a'..=b'f' => digit - b'a' + 10,
        _ => digit - b'A' + 10,
    }
}

/// The saved text: an id, "accent", or "#rrggbb" in lowercase.
impl fmt::Display for Felt {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Felt::Preset(preset) => f.write_str(preset.id),
            Felt::Accent => f.write_str(ACCENT),
            Felt::Custom([r, g, b]) => write!(f, "#{r:02x}{g:02x}{b:02x}"),
        }
    }
}

impl Serialize for Felt {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.collect_str(self)
    }
}

/// A saved value that is not a colour (a hand-edited file, a newer Tack's)
/// reads as the default: board.json must still load.
impl<'de> Deserialize<'de> for Felt {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        #[derive(Deserialize)]
        #[serde(untagged)]
        enum Saved {
            Text(String),
            Other(IgnoredAny),
        }
        Ok(match Saved::deserialize(deserializer)? {
            Saved::Text(text) => Felt::parse(&text),
            Saved::Other(_) => Felt::default(),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn green_is_the_default() {
        assert_eq!(Felt::default().to_string(), "green");
        assert_eq!(PRESETS[0].hex, "#24503f");
    }

    #[test]
    fn every_preset_round_trips_by_its_id() {
        for preset in &PRESETS {
            let felt = Felt::try_parse(preset.id).unwrap();
            assert_eq!(felt, Felt::Preset(preset));
            assert_eq!(felt.to_string(), preset.id);
        }
    }

    #[test]
    fn the_presets_are_well_formed_and_distinct() {
        for (n, preset) in PRESETS.iter().enumerate() {
            assert!(preset.id.bytes().all(|b| b.is_ascii_lowercase()), "{}", preset.id);
            assert_ne!(preset.id, ACCENT);
            // A preset's hex is a valid custom colour, already in canonical form.
            assert_eq!(Felt::try_parse(preset.hex).unwrap().to_string(), preset.hex);
            for other in &PRESETS[n + 1..] {
                assert_ne!(preset.id, other.id);
                assert_ne!(preset.hex, other.hex);
            }
        }
    }

    #[test]
    fn accent_is_a_value_of_its_own() {
        assert_eq!(Felt::try_parse("accent"), Some(Felt::Accent));
        assert_eq!(Felt::Accent.to_string(), "accent");
    }

    #[test]
    fn a_custom_colour_is_kept_as_seven_lowercase_characters() {
        assert_eq!(Felt::parse("#A1B2C3").to_string(), "#a1b2c3");
        assert_eq!(Felt::parse("#a1b2c3"), Felt::Custom([0xa1, 0xb2, 0xc3]));
        assert_eq!(Felt::parse("#000000").to_string(), "#000000");
        assert_eq!(Felt::parse("#FFFFFF").to_string(), "#ffffff");
    }

    #[test]
    fn anything_malformed_is_not_a_colour_and_falls_back() {
        let bad = [
            "",
            "#",
            "#fff",
            "#12345",
            "#1234567",
            "#12345g",
            "123456",
            "##123456",
            " #123456",
            "#123456 ",
            "Green",
            "GREEN",
            "Accent",
            "red",
            "rgb(1,2,3)",
            "#\u{e9}12345",
            "#12345\u{e9}",
            "\u{1F600}",
            "#\u{1F600}\u{1F600}",
            "\0",
        ];
        for text in bad {
            assert_eq!(Felt::try_parse(text), None, "{text:?}");
            assert_eq!(Felt::parse(text), Felt::default(), "{text:?}");
        }
    }

    #[test]
    fn it_is_saved_as_a_plain_string() {
        for text in ["green", "plum", "accent", "#12ab34"] {
            let json = serde_json::to_string(&Felt::parse(text)).unwrap();
            assert_eq!(json, format!("\"{text}\""));
            assert_eq!(serde_json::from_str::<Felt>(&json).unwrap().to_string(), text);
        }
        assert_eq!(serde_json::from_str::<Felt>("\"#ABCDEF\"").unwrap().to_string(), "#abcdef");
    }

    #[test]
    fn a_saved_value_that_is_not_a_colour_reads_as_the_default() {
        for json in ["\"mauve\"", "\"#12\"", "\"\"", "null", "3", "true", "[1]", "{\"a\":1}"] {
            assert_eq!(serde_json::from_str::<Felt>(json).unwrap(), Felt::default(), "{json}");
        }
    }
}
