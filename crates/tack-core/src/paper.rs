//! The colour of the paper notes: the user's "Note paper" setting. A colour
//! from a short list, every one pale enough that the dark ink stays easy to
//! read (the UI turns the colour into the whole note, in both themes).
//!
//! It is saved and sent to the UI as one plain string, a colour's id. The
//! list, with the swatch colour each one is shown as, is here and nowhere
//! else; the UI gets it from the backend.

use std::fmt;

use serde::{Deserialize, Deserializer, Serialize, Serializer};

use crate::lenient;

/// A paper colour the user can pick from the list.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
pub struct PaperPreset {
    /// What is saved: lowercase letters.
    pub id: &'static str,
    /// What the list shows.
    pub name: &'static str,
    /// The paper in a light theme, "#rrggbb"; the UI dims it for a dark one.
    pub hex: &'static str,
}

/// Every colour, in the order the list shows them. The first is the default
/// (the white index card the notes have always been).
pub const PRESETS: [PaperPreset; 6] = [
    PaperPreset { id: "white", name: "White", hex: "#fffefa" },
    PaperPreset { id: "cream", name: "Cream", hex: "#faf0d7" },
    PaperPreset { id: "butter", name: "Butter", hex: "#fff0a8" },
    PaperPreset { id: "blush", name: "Blush", hex: "#fadadd" },
    PaperPreset { id: "sky", name: "Sky", hex: "#d6e8fa" },
    PaperPreset { id: "mint", name: "Mint", hex: "#d5f0de" },
];

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct NotePaper(&'static PaperPreset);

impl Default for NotePaper {
    fn default() -> NotePaper {
        NotePaper(&PRESETS[0])
    }
}

impl NotePaper {
    /// A colour's id exactly; anything else is `None`.
    pub fn try_parse(text: &str) -> Option<NotePaper> {
        PRESETS.iter().find(|p| p.id == text).map(NotePaper)
    }

    /// As [`NotePaper::try_parse`]; anything unknown is the default.
    pub fn parse(text: &str) -> NotePaper {
        NotePaper::try_parse(text).unwrap_or_default()
    }

    /// The saved text.
    pub fn id(self) -> &'static str {
        self.0.id
    }
}

impl fmt::Display for NotePaper {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.id())
    }
}

impl Serialize for NotePaper {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(self.id())
    }
}

/// A saved value that is not a colour on the list (a hand-edited file, a
/// newer Tack's) reads as the default: board.json must still load.
impl<'de> Deserialize<'de> for NotePaper {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        lenient::string_or_default(deserializer, NotePaper::try_parse)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn white_is_the_default_and_the_card_it_always_was() {
        assert_eq!(NotePaper::default().id(), "white");
        assert_eq!(PRESETS[0].hex, "#fffefa");
    }

    #[test]
    fn every_colour_round_trips_by_its_id() {
        for preset in &PRESETS {
            let paper = NotePaper::try_parse(preset.id).unwrap();
            assert_eq!(paper.id(), preset.id);
            assert_eq!(paper, NotePaper(preset));
        }
    }

    #[test]
    fn the_presets_are_well_formed_and_distinct() {
        for (n, preset) in PRESETS.iter().enumerate() {
            assert!(preset.id.bytes().all(|b| b.is_ascii_lowercase()), "{}", preset.id);
            assert!(!preset.name.is_empty());
            let digits = preset.hex.strip_prefix('#').unwrap();
            assert!(
                digits.len() == 6 && digits.bytes().all(|b| matches!(b, b'0'..=b'9' | b'a'..=b'f')),
                "{}",
                preset.hex
            );
            for other in &PRESETS[n + 1..] {
                assert_ne!(preset.id, other.id);
                assert_ne!(preset.hex, other.hex);
            }
        }
    }

    #[test]
    fn every_paper_is_pale_enough_for_dark_ink() {
        // The relative luminance of the paper. The note's ink (28, 32, 42,
        // at 86%) reads on anything this pale at 7:1 or better; the UI's
        // tests check the ratios themselves.
        for preset in &PRESETS {
            let channel = |i: usize| {
                let v = f64::from(u8::from_str_radix(&preset.hex[i..i + 2], 16).unwrap()) / 255.0;
                if v <= 0.04045 {
                    v / 12.92
                } else {
                    ((v + 0.055) / 1.055).powf(2.4)
                }
            };
            let luminance = 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
            assert!(luminance > 0.6, "{} is too dark: {luminance:.2}", preset.id);
        }
    }

    #[test]
    fn anything_else_is_not_a_colour_and_falls_back() {
        for text in ["", "Blush", "BLUSH", " blush", "pink", "#fadadd", "accent", "\u{1F600}", "\0"] {
            assert_eq!(NotePaper::try_parse(text), None, "{text:?}");
            assert_eq!(NotePaper::parse(text), NotePaper::default(), "{text:?}");
        }
    }

    #[test]
    fn it_is_saved_as_a_plain_string() {
        for preset in &PRESETS {
            let json = serde_json::to_string(&NotePaper::parse(preset.id)).unwrap();
            assert_eq!(json, format!("\"{}\"", preset.id));
            assert_eq!(serde_json::from_str::<NotePaper>(&json).unwrap().id(), preset.id);
        }
    }

    #[test]
    fn a_saved_value_that_is_not_a_colour_reads_as_the_default() {
        for json in ["\"pink\"", "\"SKY\"", "\"\"", "null", "3", "true", "[1]", "{\"a\":1}"] {
            assert_eq!(serde_json::from_str::<NotePaper>(json).unwrap(), NotePaper::default(), "{json}");
        }
    }
}
