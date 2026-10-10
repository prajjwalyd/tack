//! The board's frame: the user's "Frame" setting. A finish for the metal
//! rails around the felt, chosen from a short list. The finishes are tuned
//! by hand (lit gradients for the light and the dark theme), so there is no
//! custom colour.
//!
//! It is saved and sent to the UI as one plain string, a finish's id. The
//! list is here and nowhere else (the UI gets it from the backend); the
//! gradients themselves are the UI's, in `ui/scripts/frame.js`.

use std::fmt;

use serde::{Deserialize, Deserializer, Serialize, Serializer};

use crate::lenient;

/// A finish the user can pick from the list.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
pub struct FramePreset {
    /// What is saved: lowercase letters.
    pub id: &'static str,
    /// What the list shows.
    pub name: &'static str,
}

/// Every finish, in the order the list shows them. The first is the default
/// (the brushed aluminium the board has always had).
pub const PRESETS: [FramePreset; 4] = [
    FramePreset { id: "aluminium", name: "Aluminium" },
    FramePreset { id: "graphite", name: "Graphite" },
    FramePreset { id: "brass", name: "Brass" },
    FramePreset { id: "silver", name: "Silver" },
];

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Frame(&'static FramePreset);

impl Default for Frame {
    fn default() -> Frame {
        Frame(&PRESETS[0])
    }
}

impl Frame {
    /// A finish's id exactly; anything else is `None`.
    pub fn try_parse(text: &str) -> Option<Frame> {
        PRESETS.iter().find(|p| p.id == text).map(Frame)
    }

    /// As [`Frame::try_parse`]; anything unknown is the default.
    pub fn parse(text: &str) -> Frame {
        Frame::try_parse(text).unwrap_or_default()
    }

    /// The saved text.
    pub fn id(self) -> &'static str {
        self.0.id
    }
}

impl fmt::Display for Frame {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.id())
    }
}

impl Serialize for Frame {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(self.id())
    }
}

/// A saved value that is not a finish (a hand-edited file, a newer Tack's)
/// reads as the default: board.json must still load.
impl<'de> Deserialize<'de> for Frame {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        lenient::string_or_default(deserializer, Frame::try_parse)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn aluminium_is_the_default() {
        assert_eq!(Frame::default().id(), "aluminium");
        assert_eq!(Frame::default().to_string(), "aluminium");
    }

    #[test]
    fn the_finishes_are_the_four_the_ui_draws() {
        let ids: Vec<_> = PRESETS.iter().map(|p| p.id).collect();
        assert_eq!(ids, ["aluminium", "graphite", "brass", "silver"]);
    }

    #[test]
    fn every_finish_round_trips_by_its_id() {
        for preset in &PRESETS {
            let frame = Frame::try_parse(preset.id).unwrap();
            assert_eq!(frame.id(), preset.id);
            assert_eq!(frame, Frame(preset));
        }
    }

    #[test]
    fn the_presets_are_well_formed_and_distinct() {
        for (n, preset) in PRESETS.iter().enumerate() {
            assert!(preset.id.bytes().all(|b| b.is_ascii_lowercase()), "{}", preset.id);
            assert!(!preset.name.is_empty());
            for other in &PRESETS[n + 1..] {
                assert_ne!(preset.id, other.id);
                assert_ne!(preset.name, other.name);
            }
        }
    }

    #[test]
    fn anything_else_is_not_a_finish_and_falls_back() {
        for text in ["", "Brass", "BRASS", " brass", "brass ", "gold", "#b5a642", "accent", "\u{1F600}", "\0"] {
            assert_eq!(Frame::try_parse(text), None, "{text:?}");
            assert_eq!(Frame::parse(text), Frame::default(), "{text:?}");
        }
    }

    #[test]
    fn it_is_saved_as_a_plain_string() {
        for preset in &PRESETS {
            let json = serde_json::to_string(&Frame::parse(preset.id)).unwrap();
            assert_eq!(json, format!("\"{}\"", preset.id));
            assert_eq!(serde_json::from_str::<Frame>(&json).unwrap().id(), preset.id);
        }
    }

    #[test]
    fn a_saved_value_that_is_not_a_finish_reads_as_the_default() {
        for json in ["\"gold\"", "\"BRASS\"", "\"\"", "null", "3", "true", "[1]", "{\"a\":1}"] {
            assert_eq!(serde_json::from_str::<Frame>(json).unwrap(), Frame::default(), "{json}");
        }
    }

    #[test]
    fn the_presets_serialize_as_id_and_name() {
        let json = serde_json::to_value(&PRESETS[..2]).unwrap();
        assert_eq!(
            json,
            serde_json::json!([{ "id": "aluminium", "name": "Aluminium" }, { "id": "graphite", "name": "Graphite" }])
        );
    }
}
