//! How quickly the board comes down once the pointer rests on the top edge:
//! the user's "Reveal speed" setting.
//!
//! One choice sets two things: how long the pointer must rest before the
//! board reveals ([`RevealSpeed::dwell`], used by the edge poller) and how
//! much faster the reveal animation plays ([`RevealSpeed::motion_scale`],
//! used by the UI).

use std::time::Duration;

use serde::de::IgnoredAny;
use serde::{Deserialize, Deserializer, Serialize};

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum RevealSpeed {
    /// Almost at once: the pointer need hardly rest.
    Instant,
    Fast,
    /// Long enough that passing through the edge (to a title bar or a
    /// browser tab) does not count.
    #[default]
    Normal,
}

impl RevealSpeed {
    /// How long the pointer must rest on the top edge before the board
    /// reveals.
    pub fn dwell(self) -> Duration {
        Duration::from_millis(match self {
            RevealSpeed::Instant => 100,
            RevealSpeed::Fast => 250,
            RevealSpeed::Normal => 400,
        })
    }

    /// What the UI multiplies the reveal animation's duration by, so a
    /// quicker setting also moves quicker; 1 is the original pace.
    pub fn motion_scale(self) -> f64 {
        match self {
            RevealSpeed::Instant => 0.6,
            RevealSpeed::Fast => 0.8,
            RevealSpeed::Normal => 1.0,
        }
    }

    /// "instant", "fast" or "normal", exactly; anything else is `None`.
    pub fn parse(text: &str) -> Option<RevealSpeed> {
        match text {
            "instant" => Some(RevealSpeed::Instant),
            "fast" => Some(RevealSpeed::Fast),
            "normal" => Some(RevealSpeed::Normal),
            _ => None,
        }
    }
}

/// A saved value that is not a speed (a hand-edited file, a newer Tack's)
/// reads as the default: board.json must still load.
impl<'de> Deserialize<'de> for RevealSpeed {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        #[derive(Deserialize)]
        #[serde(untagged)]
        enum Saved {
            Text(String),
            Other(IgnoredAny),
        }
        Ok(match Saved::deserialize(deserializer)? {
            Saved::Text(text) => RevealSpeed::parse(&text).unwrap_or_default(),
            Saved::Other(_) => RevealSpeed::default(),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const ALL: [RevealSpeed; 3] = [RevealSpeed::Instant, RevealSpeed::Fast, RevealSpeed::Normal];

    #[test]
    fn normal_is_the_default_and_keeps_the_original_pace() {
        assert_eq!(RevealSpeed::default(), RevealSpeed::Normal);
        assert_eq!(RevealSpeed::Normal.dwell(), Duration::from_millis(400));
        assert_eq!(RevealSpeed::Normal.motion_scale(), 1.0);
    }

    #[test]
    fn quicker_speeds_wait_less_and_move_quicker() {
        assert_eq!(RevealSpeed::Instant.dwell(), Duration::from_millis(100));
        assert_eq!(RevealSpeed::Fast.dwell(), Duration::from_millis(250));
        assert_eq!(RevealSpeed::Instant.motion_scale(), 0.6);
        assert_eq!(RevealSpeed::Fast.motion_scale(), 0.8);
        for pair in ALL.windows(2) {
            assert!(pair[0].dwell() < pair[1].dwell());
            assert!(pair[0].motion_scale() < pair[1].motion_scale());
        }
    }

    #[test]
    fn it_is_saved_as_a_lowercase_word() {
        for (speed, text) in ALL.into_iter().zip(["instant", "fast", "normal"]) {
            assert_eq!(serde_json::to_string(&speed).unwrap(), format!("\"{text}\""));
            assert_eq!(serde_json::from_str::<RevealSpeed>(&format!("\"{text}\"")).unwrap(), speed);
            assert_eq!(RevealSpeed::parse(text), Some(speed));
        }
    }

    #[test]
    fn anything_else_is_not_a_speed() {
        for bad in ["", "Fast", "FAST", " fast", "turbo", "slow", "0"] {
            assert_eq!(RevealSpeed::parse(bad), None, "{bad:?}");
            let json = serde_json::to_string(bad).unwrap();
            assert_eq!(serde_json::from_str::<RevealSpeed>(&json).unwrap(), RevealSpeed::Normal, "{bad:?}");
        }
        for odd in ["null", "7", "true", "[]", "{\"a\":1}"] {
            assert_eq!(serde_json::from_str::<RevealSpeed>(odd).unwrap(), RevealSpeed::Normal, "{odd}");
        }
    }
}
