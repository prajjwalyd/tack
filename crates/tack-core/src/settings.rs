//! The user's settings, toggled from the tray and saved in board.json.

use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    /// The pin and pop sounds.
    pub sound: bool,
    /// Resting the pointer against the top edge reveals the board.
    pub edge_reveal: bool,
}

impl Default for Settings {
    fn default() -> Self {
        Settings { sound: true, edge_reveal: true }
    }
}
