//! The network side: `tauri-plugin-updater`, driven from Rust only (the web
//! views are granted none of its commands, so they cannot start a check or
//! an install, and the page's content security policy keeps them off the
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

use crate::state;

/// The check: a small JSON file.
const CHECK_TIMEOUT: Duration = Duration::from_secs(30);
/// Reaching the server, for the check and the download.
const CONNECT_TIMEOUT: Duration = Duration::from_secs(15);
/// The whole download (the installer is a few MB): a stalled one ends.
const DOWNLOAD_TIMEOUT: Duration = Duration::from_secs(10 * 60);

// What the person is told, in the few kinds told apart. Details (a status
// code, a URL) go to the log, never to the UI.
const NETWORK: &str = "Couldn\u{2019}t reach the update server. Check your internet connection.";
const SERVER: &str = "The update server didn\u{2019}t give a usable answer. Try again later.";
const SIGNATURE: &str = "The update\u{2019}s signature could not be verified, so it was not installed.";
const LOCAL: &str = "Couldn\u{2019}t prepare the update on this PC.";
const OTHER: &str = "The update could not be completed.";

/// Asks for a version newer than this one. Blocks on the network.
pub fn check(app: &AppHandle) -> Result<Option<Update>, &'static str> {
    let exiting = app.clone();
    let updater = app
        .updater_builder()
        .timeout(CHECK_TIMEOUT)
        // GitHub redirects downloads to its CDN: every hop must be HTTPS.
        .configure_client(|client| client.https_only(true).connect_timeout(CONNECT_TIMEOUT))
        // The installer starts and Tack exits at once, without the exit
        // event that writes the board: do that first.
        .on_before_exit(move || {
            state::flush_saves();
            exiting.cleanup_before_exit();
        })
        .build()
        .map_err(|e| failed("set up", &e))?;
    let mut update = tauri::async_runtime::block_on(updater.check()).map_err(|e| failed("check", &e))?;
    if let Some(update) = &mut update {
        update.timeout = Some(DOWNLOAD_TIMEOUT);
    }
    Ok(update)
}

/// Downloads, verifies and installs, calling `on_percent` as the download
/// moves and `on_verified` once the signature checked out. The installer
/// ends this process, so success may never return.
pub fn install(
    update: &Update,
    mut on_percent: impl FnMut(u64),
    on_verified: impl FnOnce(),
) -> Result<(), &'static str> {
    let (mut bytes, mut shown) = (0u64, None);
    // `download` returns the installer only once its signature verified.
    let installer = tauri::async_runtime::block_on(update.download(
        |chunk, total| {
            bytes += chunk as u64;
            if let Some(percent) = percent(bytes, total).filter(|&p| shown != Some(p)) {
                shown = Some(percent);
                on_percent(percent);
            }
        },
        || {},
    ))
    .map_err(|e| failed("download", &e))?;
    on_verified();
    // Starts the installer and ends this process; it returns on failure only.
    update.install(installer).map_err(|e| failed("install", &e))
}

/// How much of the download is done, in whole percent; `None` if the size is unknown.
fn percent(bytes: u64, total: Option<u64>) -> Option<u64> {
    let total = total.filter(|&t| t > 0)?;
    Some((bytes.saturating_mul(100) / total).min(100))
}

/// Logs what went wrong for whoever is debugging and returns what the person
/// is told (a kind of failure, never the error's own text).
fn failed(step: &str, error: &Error) -> &'static str {
    eprintln!("tack: update {step} failed: {error}");
    classify(error)
}

/// Sorts the plugin's errors into the few the person is told apart.
fn classify(error: &Error) -> &'static str {
    match error {
        Error::Reqwest(_) => NETWORK,
        // The server answered with something else than an update.
        Error::ReleaseNotFound
        | Error::Network(_)
        | Error::Serialization(_)
        | Error::Semver(_)
        | Error::TargetNotFound(_)
        | Error::TargetsNotFound(_) => SERVER,
        // Anything that says the download is not what the key signed.
        Error::Minisign(_)
        | Error::Base64(_)
        | Error::SignatureUtf8(_)
        | Error::SignedVersionMismatch { .. }
        | Error::MissingSignedVersion => SIGNATURE,
        Error::Io(_) | Error::TempDirNotFound | Error::InvalidUpdaterFormat => LOCAL,
        _ => OTHER,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use base64::Engine;

    #[test]
    fn a_missing_or_unreadable_release_is_the_servers_answer() {
        assert_eq!(classify(&Error::ReleaseNotFound), SERVER);
        assert_eq!(classify(&Error::Network("Download request failed with status: 404".into())), SERVER);
        assert_eq!(classify(&Error::TargetNotFound("windows-x86_64".into())), SERVER);
    }

    #[test]
    fn anything_wrong_with_the_signature_is_never_installed() {
        let not_base64 = base64::engine::general_purpose::STANDARD.decode("***").unwrap_err();
        assert_eq!(classify(&Error::Base64(not_base64)), SIGNATURE);
        assert_eq!(classify(&Error::MissingSignedVersion), SIGNATURE);
        let mismatch = Error::SignedVersionMismatch { signed: "0.1.0".into(), announced: "9.9.9".into() };
        assert_eq!(classify(&mismatch), SIGNATURE);
    }

    #[test]
    fn local_problems_are_told_apart() {
        assert_eq!(classify(&Error::Io(std::io::Error::other("disk full"))), LOCAL);
        assert_eq!(classify(&Error::InvalidUpdaterFormat), LOCAL);
        assert_eq!(classify(&Error::EmptyEndpoints), OTHER);
    }

    #[test]
    fn what_the_person_reads_never_carries_the_error_text() {
        let error = Error::Network("https://example.invalid/?token=secret".into());
        let told = classify(&error);
        assert!(!told.contains("example") && !told.contains("secret"));
    }

    #[test]
    fn progress_is_whole_percent_when_the_size_is_known() {
        assert_eq!(percent(25, Some(100)), Some(25));
        assert_eq!(percent(150, Some(100)), Some(100));
        assert_eq!((percent(10, Some(0)), percent(10, None)), (None, None));
    }
}
