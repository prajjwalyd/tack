//! What the UI is told about updates: [`UpdateStatus`], sent whole (as the
//! `updates:status` event and from `update_status`) on every change.

use serde::Serialize;

/// The most characters of release notes the UI is sent. The notes come from
/// the release's `latest.json`, so they are another party's text; the UI
/// shows them as text, and this keeps a bad one from being huge.
pub const NOTES_MAX_CHARS: usize = 2000;

/// Where the updater is.
#[derive(Serialize, Clone, Copy, PartialEq, Eq, Debug)]
#[serde(rename_all = "camelCase")]
pub enum Phase {
    /// Nothing has been checked yet.
    Idle,
    Checking,
    UpToDate,
    /// A newer version is ready to install.
    Available,
    Downloading,
    /// Downloaded and verified; the installer is starting and Tack is about to quit.
    Installing,
    Error,
}

impl Phase {
    /// Work is going on, so another check or install must not start.
    pub fn is_busy(self) -> bool {
        matches!(self, Phase::Checking | Phase::Downloading | Phase::Installing)
    }
}

#[derive(Serialize, Clone, PartialEq, Debug)]
#[serde(rename_all = "camelCase")]
pub struct UpdateStatus {
    pub state: Phase,
    /// The new version, as "0.2.0", once one is known.
    pub version: Option<String>,
    /// The release notes: untrusted text, at most [`NOTES_MAX_CHARS`] long.
    pub notes: Option<String>,
    /// How much of the download is done, 0 to 1; `None` if it is not
    /// downloading or the size is not known.
    pub progress: Option<f64>,
    /// A short message for the person, never a raw error.
    pub error: Option<String>,
    /// When the last successful check finished, in ms since the Unix epoch.
    pub checked_at: Option<u64>,
}

impl UpdateStatus {
    pub fn idle() -> UpdateStatus {
        UpdateStatus { state: Phase::Idle, version: None, notes: None, progress: None, error: None, checked_at: None }
    }
}

/// The notes as the UI gets them: nothing if blank, otherwise cut to
/// [`NOTES_MAX_CHARS`] characters (with an ellipsis if it was longer). Only
/// the length is touched: the UI never treats the text as markup.
pub fn clean_notes(notes: Option<String>) -> Option<String> {
    let notes = notes?;
    if notes.trim().is_empty() {
        return None;
    }
    match notes.char_indices().nth(NOTES_MAX_CHARS) {
        None => Some(notes),
        Some((cut, _)) => Some(format!("{}\u{2026}", &notes[..cut])),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn blank_notes_are_nothing() {
        assert_eq!(clean_notes(None), None);
        assert_eq!(clean_notes(Some(String::new())), None);
        assert_eq!(clean_notes(Some(" \r\n\t".into())), None);
    }

    #[test]
    fn short_notes_are_left_as_they_are() {
        let notes = "## Fixes\n- <b>one</b> & two\n";
        assert_eq!(clean_notes(Some(notes.into())).as_deref(), Some(notes));
        let exact = "a".repeat(NOTES_MAX_CHARS);
        assert_eq!(clean_notes(Some(exact.clone())), Some(exact));
    }

    #[test]
    fn long_notes_are_cut_on_a_character_boundary() {
        let long = "\u{e9}".repeat(NOTES_MAX_CHARS + 50);
        let cut = clean_notes(Some(long)).unwrap();
        assert_eq!(cut.chars().count(), NOTES_MAX_CHARS + 1);
        assert!(cut.ends_with('\u{2026}'));
    }

    #[test]
    fn the_status_is_sent_in_camel_case() {
        let status = UpdateStatus {
            state: Phase::UpToDate,
            version: Some("0.2.0".into()),
            notes: None,
            progress: Some(0.5),
            error: None,
            checked_at: Some(7),
        };
        let json = serde_json::to_value(&status).unwrap();
        assert_eq!(
            json,
            serde_json::json!({
                "state": "upToDate", "version": "0.2.0", "notes": null,
                "progress": 0.5, "error": null, "checkedAt": 7
            })
        );
        assert_eq!(serde_json::to_value(Phase::Idle).unwrap(), "idle");
    }
}
