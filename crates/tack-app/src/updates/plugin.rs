//! The real [`Source`]: `tauri-plugin-updater`, driven from Rust only (the
//! web views are granted none of its commands, so they cannot start a check
//! or an install, and the page's content security policy keeps them off the
//! network).
//!
//! What the plugin does, which `tauri.conf.json` sets up (and `config_guard`
//! tests): fetches `latest.json` from the one HTTPS endpoint, takes a version
//! only if it is newer than this one, downloads the installer, and installs
//! it only if its minisign signature verifies against the public key built
//! into the app and was made for the announced version (`requireSignedVersion`,
//! so an old signed installer cannot be passed off as a new release). What
//! this file adds: HTTPS for every redirect, timeouts, and saving the board
//! before the installer takes over.

use std::time::Duration;

use tauri::AppHandle;
use tauri_plugin_updater::{Error, Update, UpdaterExt};

use super::machine::{Failure, Found, Progress, Source};
use crate::state;

/// The check: a small JSON file.
const CHECK_TIMEOUT: Duration = Duration::from_secs(30);
/// Reaching the server, for the check and the download.
const CONNECT_TIMEOUT: Duration = Duration::from_secs(15);
/// The whole download (the installer is a few MB): a stalled one ends.
const DOWNLOAD_TIMEOUT: Duration = Duration::from_secs(10 * 60);

pub struct PluginSource {
    app: AppHandle,
}

impl PluginSource {
    pub fn new(app: AppHandle) -> PluginSource {
        PluginSource { app }
    }

    fn updater(&self) -> Result<tauri_plugin_updater::Updater, Failure> {
        let app = self.app.clone();
        self.app
            .updater_builder()
            .timeout(CHECK_TIMEOUT)
            // GitHub redirects downloads to its CDN: every hop must be HTTPS.
            .configure_client(|client| client.https_only(true).connect_timeout(CONNECT_TIMEOUT))
            // The installer starts and Tack exits at once, without the exit
            // event that writes the board: do that first.
            .on_before_exit(move || {
                state::flush_saves();
                app.cleanup_before_exit();
            })
            .build()
            .map_err(|e| failed("set up", &e))
    }
}

impl Source for PluginSource {
    type Pending = Update;

    fn check(&self) -> Result<Option<Found<Update>>, Failure> {
        let checked = tauri::async_runtime::block_on(self.updater()?.check());
        match checked.map_err(|e| failed("check", &e))? {
            Some(mut update) => {
                update.timeout = Some(DOWNLOAD_TIMEOUT);
                Ok(Some(Found { version: update.version.clone(), notes: update.body.clone(), pending: update }))
            }
            None => Ok(None),
        }
    }

    fn install(&self, update: &Update, on_progress: &mut dyn FnMut(Progress)) -> Result<(), Failure> {
        let mut bytes = 0u64;
        // `download` returns the installer only once its signature verified.
        let installer = tauri::async_runtime::block_on(update.download(
            |chunk, total| {
                bytes += chunk as u64;
                on_progress(Progress::Received { bytes, total });
            },
            || {},
        ))
        .map_err(|e| failed("download", &e))?;
        on_progress(Progress::Installing);
        // Starts the installer and ends this process; it returns on failure only.
        update.install(installer).map_err(|e| failed("install", &e))
    }
}

/// Logs what went wrong for whoever is debugging and returns what the person
/// is told (a kind of failure, never the error's own text).
fn failed(step: &str, error: &Error) -> Failure {
    eprintln!("tack: update {step} failed: {error}");
    classify(error)
}

/// Sorts the plugin's errors into the few the person is told apart.
fn classify(error: &Error) -> Failure {
    match error {
        Error::Reqwest(_) => Failure::Network,
        // The server answered with something else than an update.
        Error::ReleaseNotFound
        | Error::Network(_)
        | Error::Serialization(_)
        | Error::Semver(_)
        | Error::TargetNotFound(_)
        | Error::TargetsNotFound(_) => Failure::Server,
        // Anything that says the download is not what the key signed.
        Error::Minisign(_)
        | Error::Base64(_)
        | Error::SignatureUtf8(_)
        | Error::SignedVersionMismatch { .. }
        | Error::MissingSignedVersion => Failure::Signature,
        Error::Io(_) | Error::TempDirNotFound | Error::InvalidUpdaterFormat => Failure::Local,
        _ => Failure::Other,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use base64::Engine;

    #[test]
    fn a_missing_or_unreadable_release_is_the_servers_answer() {
        assert_eq!(classify(&Error::ReleaseNotFound), Failure::Server);
        assert_eq!(classify(&Error::Network("Download request failed with status: 404".into())), Failure::Server);
        assert_eq!(classify(&Error::TargetNotFound("windows-x86_64".into())), Failure::Server);
    }

    #[test]
    fn anything_wrong_with_the_signature_is_never_installed() {
        let not_base64 = base64::engine::general_purpose::STANDARD.decode("***").unwrap_err();
        assert_eq!(classify(&Error::Base64(not_base64)), Failure::Signature);
        assert_eq!(classify(&Error::MissingSignedVersion), Failure::Signature);
        let mismatch = Error::SignedVersionMismatch { signed: "0.1.0".into(), announced: "9.9.9".into() };
        assert_eq!(classify(&mismatch), Failure::Signature);
    }

    #[test]
    fn local_problems_are_told_apart() {
        assert_eq!(classify(&Error::Io(std::io::Error::other("disk full"))), Failure::Local);
        assert_eq!(classify(&Error::InvalidUpdaterFormat), Failure::Local);
        assert_eq!(classify(&Error::EmptyEndpoints), Failure::Other);
    }

    #[test]
    fn what_the_person_reads_never_carries_the_error_text() {
        let error = Error::Network("https://example.invalid/?token=secret".into());
        let told = classify(&error).message();
        assert!(!told.contains("example") && !told.contains("secret"));
    }
}
