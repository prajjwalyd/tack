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

    /// A check began: all but the last good time is dropped.
    pub fn checking(&self) -> UpdateStatus {
        UpdateStatus { state: Phase::Checking, checked_at: self.checked_at, ..Self::idle() }
    }

    pub fn found(version: String, notes: Option<String>, now: u64) -> UpdateStatus {
        UpdateStatus {
            state: Phase::Available,
            version: Some(version),
            notes: clean_notes(notes),
            checked_at: Some(now),
            ..Self::idle()
        }
    }

    pub fn up_to_date(now: u64) -> UpdateStatus {
        UpdateStatus { state: Phase::UpToDate, checked_at: Some(now), ..Self::idle() }
    }

    /// A check or an install failed: `message` is shown, the last good time stays.
    pub fn failed(&self, message: &str) -> UpdateStatus {
        UpdateStatus { state: Phase::Error, notes: None, progress: None, error: Some(message.into()), ..self.clone() }
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
    fn notes_are_blank_cut_or_left_alone() {
        assert_eq!(clean_notes(None), None);
        assert_eq!(clean_notes(Some(" \r\n\t".into())), None);
        let notes = "## Fixes\n- <b>one</b> & two\n";
        assert_eq!(clean_notes(Some(notes.into())).as_deref(), Some(notes));
        let cut = clean_notes(Some("\u{e9}".repeat(NOTES_MAX_CHARS + 50))).unwrap();
        assert_eq!(cut.chars().count(), NOTES_MAX_CHARS + 1);
        assert!(cut.ends_with('\u{2026}'));
    }

    #[test]
    fn a_failure_keeps_the_last_good_time_and_drops_the_rest() {
        let found = UpdateStatus::found("0.2.0".into(), Some("new".into()), 1_000);
        assert_eq!((found.state, found.checked_at), (Phase::Available, Some(1_000)));
        let checking = found.checking();
        assert_eq!((checking.state, checking.version.clone(), checking.notes.clone()), (Phase::Checking, None, None));
        let failed = checking.failed("no luck");
        assert_eq!(
            (failed.state, failed.error.as_deref(), failed.checked_at),
            (Phase::Error, Some("no luck"), Some(1_000))
        );
        assert_eq!(UpdateStatus::up_to_date(2_000).checked_at, Some(2_000));
    }

    #[test]
    fn a_failed_install_keeps_the_version_but_not_the_notes() {
        let downloading = UpdateStatus {
            state: Phase::Downloading,
            progress: Some(0.4),
            ..UpdateStatus::found("0.2.0".into(), Some("new".into()), 5)
        };
        let failed = downloading.failed("no luck");
        assert_eq!((failed.version.as_deref(), failed.notes, failed.progress), (Some("0.2.0"), None, None));
    }

    #[test]
    fn the_status_is_sent_in_camel_case() {
        let status = UpdateStatus { progress: Some(0.5), ..UpdateStatus::up_to_date(7) };
        assert_eq!(
            serde_json::to_value(&status).unwrap(),
            serde_json::json!({
                "state": "upToDate", "version": null, "notes": null,
                "progress": 0.5, "error": null, "checkedAt": 7
            })
        );
    }
}
