//! Reading a saved choice that may not be one any more.
//!
//! board.json is hand-editable and may come from a newer Tack, so a setting
//! that is not a value Tack knows must read as its default and never stop the
//! file loading. The choices that are a plain string (a frame finish, a note
//! paper) share this.

use serde::de::IgnoredAny;
use serde::{Deserialize, Deserializer};

/// The saved string passed through `parse`; anything that is not a string, or
/// that `parse` does not know, is the default.
pub(crate) fn string_or_default<'de, D, T>(
    deserializer: D,
    parse: impl FnOnce(&str) -> Option<T>,
) -> Result<T, D::Error>
where
    D: Deserializer<'de>,
    T: Default,
{
    #[derive(Deserialize)]
    #[serde(untagged)]
    enum Saved {
        Text(String),
        Other(IgnoredAny),
    }
    Ok(match Saved::deserialize(deserializer)? {
        Saved::Text(text) => parse(&text).unwrap_or_default(),
        Saved::Other(_) => T::default(),
    })
}
