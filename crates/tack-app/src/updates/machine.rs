//! The updater's state machine. It knows nothing of Tauri or the network: a
//! [`Source`] checks for a newer version and installs it, and [`Updater`]
//! keeps the [`UpdateStatus`] the UI is told, announcing every change through
//! the callback it was built with. The real source is `plugin.rs`; the tests
//! use a scripted one.
//!
//! Checks and installs block (a request, a download), so they run on a
//! thread that can wait. The state lock is only held to change the status,
//! never across the source, and the callback runs once it is released.

use std::sync::{Mutex, MutexGuard};
use std::time::Instant;

use super::status::{clean_notes, Phase, UpdateStatus};

/// The least time between two announcements of download progress. State
/// changes are always announced at once.
const PROGRESS_GAP_MS: u64 = 100;

/// Why a check or an install failed, in the few kinds the person is told
/// apart. The details (a status code, a URL) go to the log, not to the UI.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Failure {
    /// The server could not be reached, or the connection broke.
    Network,
    /// The server answered, but not with an update (a 404, a rate limit, a
    /// manifest without this PC's build).
    Server,
    /// The update did not verify: Tack's built-in key did not sign it, or it
    /// is not the version it claims to be. It is never installed.
    Signature,
    /// This PC could not store or start the installer.
    Local,
    Other,
}

impl Failure {
    /// A short, calm message for the status.
    pub fn message(self) -> &'static str {
        match self {
            Failure::Network => "Couldn\u{2019}t reach the update server. Check your internet connection.",
            Failure::Server => "The update server didn\u{2019}t give a usable answer. Try again later.",
            Failure::Signature => "The update\u{2019}s signature could not be verified, so it was not installed.",
            Failure::Local => "Couldn\u{2019}t prepare the update on this PC.",
            Failure::Other => "The update could not be completed.",
        }
    }
}

/// A newer version a check found.
pub struct Found<P> {
    pub version: String,
    pub notes: Option<String>,
    /// What the source needs to install it later.
    pub pending: P,
}

/// How an install is going.
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Progress {
    /// `bytes` so far of `total`, if the server said how big it is.
    Received { bytes: u64, total: Option<u64> },
    /// Downloaded and verified; the installer is about to start.
    Installing,
}

/// Where updates come from.
pub trait Source: Send + Sync + 'static {
    /// What a check keeps for the install that may follow.
    type Pending: Clone + Send + 'static;

    /// Asks for a version newer than this one: `None` if there is none.
    fn check(&self) -> Result<Option<Found<Self::Pending>>, Failure>;

    /// Downloads, verifies and installs it, reporting progress. The real
    /// installer replaces this process, so success may never return.
    fn install(&self, pending: &Self::Pending, on_progress: &mut dyn FnMut(Progress)) -> Result<(), Failure>;
}

struct Inner<P> {
    status: UpdateStatus,
    /// What `Available` can be installed from.
    pending: Option<P>,
    /// When a check last started, whatever came of it: the schedule's
    /// anchor, so a failure waits for the next turn instead of retrying.
    last_attempt: Option<u64>,
}

pub struct Updater<S: Source> {
    source: S,
    inner: Mutex<Inner<S::Pending>>,
    /// Called with every new status, off the lock.
    announce: Box<dyn Fn(&UpdateStatus) + Send + Sync>,
    /// The wall clock, in ms since the Unix epoch.
    now: Box<dyn Fn() -> u64 + Send + Sync>,
}

impl<S: Source> Updater<S> {
    pub fn new(
        source: S,
        announce: impl Fn(&UpdateStatus) + Send + Sync + 'static,
        now: impl Fn() -> u64 + Send + Sync + 'static,
    ) -> Updater<S> {
        Updater {
            source,
            inner: Mutex::new(Inner { status: UpdateStatus::idle(), pending: None, last_attempt: None }),
            announce: Box::new(announce),
            now: Box::new(now),
        }
    }

    pub fn status(&self) -> UpdateStatus {
        self.lock().status.clone()
    }

    /// The phase and when a check last started, for the schedule.
    pub fn schedule_state(&self) -> (Phase, Option<u64>) {
        let inner = self.lock();
        (inner.status.state, inner.last_attempt)
    }

    /// Checks now, whatever the schedule says. If a check or an install is
    /// already going, changes nothing and returns the status as it is.
    pub fn check(&self) -> UpdateStatus {
        let checking = {
            let mut inner = self.lock();
            if inner.status.state.is_busy() {
                return inner.status.clone();
            }
            inner.last_attempt = Some((self.now)());
            inner.status = UpdateStatus {
                state: Phase::Checking,
                version: None,
                notes: None,
                progress: None,
                error: None,
                checked_at: inner.status.checked_at,
            };
            inner.pending = None;
            inner.status.clone()
        };
        (self.announce)(&checking);

        let result = self.source.check();
        let done = {
            let mut inner = self.lock();
            let checked_at = inner.status.checked_at;
            match result {
                Ok(Some(found)) => {
                    inner.pending = Some(found.pending);
                    inner.status = UpdateStatus {
                        state: Phase::Available,
                        version: Some(found.version),
                        notes: clean_notes(found.notes),
                        progress: None,
                        error: None,
                        checked_at: Some((self.now)()),
                    };
                }
                Ok(None) => {
                    inner.status =
                        UpdateStatus { state: Phase::UpToDate, checked_at: Some((self.now)()), ..UpdateStatus::idle() };
                }
                Err(failure) => {
                    inner.status = UpdateStatus {
                        state: Phase::Error,
                        error: Some(failure.message().to_string()),
                        checked_at,
                        ..UpdateStatus::idle()
                    };
                }
            }
            inner.status.clone()
        };
        (self.announce)(&done);
        done
    }

    /// Installs the version the last check found and returns the status it
    /// ends in. Does nothing unless one is waiting. On success the installer
    /// takes over and Tack quits (and restarts); on failure the status says
    /// so and the update has to be found again.
    pub fn install(&self) -> UpdateStatus {
        let (pending, downloading) = {
            let mut inner = self.lock();
            if inner.status.state != Phase::Available {
                return inner.status.clone();
            }
            let Some(pending) = inner.pending.clone() else { return inner.status.clone() };
            inner.status.state = Phase::Downloading;
            inner.status.progress = None;
            inner.status.error = None;
            (pending, inner.status.clone())
        };
        (self.announce)(&downloading);

        let started = Instant::now();
        let mut gap = Throttle::default();
        let result = self.source.install(&pending, &mut |progress| match progress {
            Progress::Received { bytes, total } => {
                let status = self.change(|s| s.progress = fraction(bytes, total));
                let done = status.progress == Some(1.0);
                if gap.allow(started.elapsed().as_millis() as u64, done) {
                    (self.announce)(&status);
                }
            }
            Progress::Installing => {
                let status = self.change(|s| {
                    s.state = Phase::Installing;
                    s.progress = Some(1.0);
                });
                (self.announce)(&status);
            }
        });

        let status = match result {
            // Only reached if the source does not quit the app itself.
            Ok(()) => self.change(|s| {
                s.state = Phase::Installing;
                s.progress = Some(1.0);
            }),
            Err(failure) => {
                self.lock().pending = None;
                self.change(|s| {
                    s.state = Phase::Error;
                    s.progress = None;
                    s.notes = None;
                    s.error = Some(failure.message().to_string());
                })
            }
        };
        (self.announce)(&status);
        status
    }

    fn change(&self, change: impl FnOnce(&mut UpdateStatus)) -> UpdateStatus {
        let mut inner = self.lock();
        change(&mut inner.status);
        inner.status.clone()
    }

    fn lock(&self) -> MutexGuard<'_, Inner<S::Pending>> {
        // A panic in another thread must not take the updater down with it.
        self.inner.lock().unwrap_or_else(|p| p.into_inner())
    }
}

/// How much of the download is done, 0 to 1; `None` if the size is unknown.
fn fraction(bytes: u64, total: Option<u64>) -> Option<f64> {
    let total = total.filter(|&t| t > 0)?;
    Some((bytes as f64 / total as f64).clamp(0.0, 1.0))
}

/// Lets progress through at most every [`PROGRESS_GAP_MS`], and always the
/// last of it.
#[derive(Default)]
struct Throttle {
    last: Option<u64>,
}

impl Throttle {
    fn allow(&mut self, now_ms: u64, done: bool) -> bool {
        let due = done || self.last.is_none_or(|last| now_ms.saturating_sub(last) >= PROGRESS_GAP_MS);
        if due {
            self.last = Some(now_ms);
        }
        due
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::VecDeque;
    use std::sync::{Arc, Mutex};

    type Checks = VecDeque<Result<Option<Found<u32>>, Failure>>;

    /// A source that answers from a script and counts what it is asked.
    struct Scripted {
        checks: Mutex<Checks>,
        /// What an install reports, then how it ends.
        install: Mutex<(Vec<Progress>, Result<(), Failure>)>,
        calls: Mutex<(u32, Vec<u32>)>,
    }

    impl Scripted {
        fn new(checks: Checks) -> Scripted {
            Scripted { checks: Mutex::new(checks), install: Mutex::new((vec![], Ok(()))), calls: Mutex::default() }
        }
    }

    impl Source for Arc<Scripted> {
        type Pending = u32;

        fn check(&self) -> Result<Option<Found<u32>>, Failure> {
            self.calls.lock().unwrap().0 += 1;
            self.checks.lock().unwrap().pop_front().expect("an unexpected check")
        }

        fn install(&self, pending: &u32, on_progress: &mut dyn FnMut(Progress)) -> Result<(), Failure> {
            self.calls.lock().unwrap().1.push(*pending);
            let (events, result) = self.install.lock().unwrap().clone();
            events.into_iter().for_each(on_progress);
            result
        }
    }

    fn found(version: &str) -> Result<Option<Found<u32>>, Failure> {
        Ok(Some(Found { version: version.into(), notes: Some("What is new".into()), pending: 7 }))
    }

    /// An updater over `source` whose announcements are collected.
    fn updater(source: &Arc<Scripted>) -> (Updater<Arc<Scripted>>, Arc<Mutex<Vec<UpdateStatus>>>) {
        let heard = Arc::new(Mutex::new(vec![]));
        let sink = heard.clone();
        let updater = Updater::new(source.clone(), move |s| sink.lock().unwrap().push(s.clone()), || 1_000);
        (updater, heard)
    }

    fn states(heard: &Mutex<Vec<UpdateStatus>>) -> Vec<Phase> {
        heard.lock().unwrap().iter().map(|s| s.state).collect()
    }

    #[test]
    fn a_new_version_is_announced_with_its_notes() {
        let source = Arc::new(Scripted::new([found("0.2.0")].into()));
        let (updater, heard) = updater(&source);
        let status = updater.check();
        assert_eq!(status.state, Phase::Available);
        assert_eq!(status.version.as_deref(), Some("0.2.0"));
        assert_eq!(status.notes.as_deref(), Some("What is new"));
        assert_eq!(status.checked_at, Some(1_000));
        assert_eq!(states(&heard), [Phase::Checking, Phase::Available]);
        assert_eq!(updater.status(), status);
    }

    #[test]
    fn blank_notes_and_long_notes_are_cleaned() {
        let long = "x".repeat(10_000);
        let source = Arc::new(Scripted::new(
            [
                Ok(Some(Found { version: "0.2.0".into(), notes: Some("  ".into()), pending: 1 })),
                Ok(Some(Found { version: "0.2.0".into(), notes: Some(long), pending: 1 })),
            ]
            .into(),
        ));
        let (updater, _) = updater(&source);
        assert_eq!(updater.check().notes, None);
        let notes = updater.check().notes.unwrap();
        assert!(notes.chars().count() < 3000);
    }

    #[test]
    fn no_newer_version_is_up_to_date() {
        let source = Arc::new(Scripted::new([Ok(None)].into()));
        let (updater, heard) = updater(&source);
        let status = updater.check();
        assert_eq!((status.state, status.version, status.checked_at), (Phase::UpToDate, None, Some(1_000)));
        assert_eq!(states(&heard), [Phase::Checking, Phase::UpToDate]);
    }

    #[test]
    fn a_failed_check_is_an_error_that_keeps_the_last_good_time() {
        let source = Arc::new(Scripted::new([Ok(None), Err(Failure::Network)].into()));
        let (updater, _) = updater(&source);
        updater.check();
        let status = updater.check();
        assert_eq!(status.state, Phase::Error);
        assert_eq!(status.error.as_deref(), Some(Failure::Network.message()));
        assert_eq!(status.checked_at, Some(1_000));
        assert_eq!(updater.schedule_state(), (Phase::Error, Some(1_000)));
    }

    #[test]
    fn a_failed_check_still_counts_as_an_attempt() {
        let source = Arc::new(Scripted::new([Err(Failure::Server)].into()));
        let (updater, _) = updater(&source);
        assert_eq!(updater.schedule_state(), (Phase::Idle, None));
        updater.check();
        assert_eq!(updater.schedule_state().1, Some(1_000));
    }

    #[test]
    fn an_install_goes_from_downloading_to_installing() {
        let source = Arc::new(Scripted::new([found("0.2.0")].into()));
        *source.install.lock().unwrap() =
            (vec![Progress::Received { bytes: 50, total: Some(100) }, Progress::Installing], Ok(()));
        let (updater, heard) = updater(&source);
        updater.check();
        heard.lock().unwrap().clear();
        updater.install();
        let heard = heard.lock().unwrap();
        assert_eq!(heard[0].state, Phase::Downloading);
        assert_eq!(heard[0].progress, None);
        assert_eq!(heard[1].progress, Some(0.5));
        let last = heard.last().unwrap();
        assert_eq!((last.state, last.progress), (Phase::Installing, Some(1.0)));
        assert_eq!(last.version.as_deref(), Some("0.2.0"));
        assert_eq!(source.calls.lock().unwrap().1, [7]);
    }

    #[test]
    fn a_failed_install_is_an_error_and_forgets_the_update() {
        let source = Arc::new(Scripted::new([found("0.2.0")].into()));
        *source.install.lock().unwrap() =
            (vec![Progress::Received { bytes: 10, total: None }], Err(Failure::Signature));
        let (updater, _) = updater(&source);
        updater.check();
        updater.install();
        let status = updater.status();
        assert_eq!(status.state, Phase::Error);
        assert_eq!(status.error.as_deref(), Some(Failure::Signature.message()));
        assert_eq!((status.progress, status.notes), (None, None));
        assert_eq!(status.version.as_deref(), Some("0.2.0"));
        // Nothing is left to install: a second try does not reach the source.
        updater.install();
        assert_eq!(source.calls.lock().unwrap().1.len(), 1);
    }

    #[test]
    fn installing_needs_an_update_to_install() {
        let source = Arc::new(Scripted::new([Ok(None)].into()));
        let (updater, heard) = updater(&source);
        updater.install();
        updater.check();
        heard.lock().unwrap().clear();
        updater.install();
        assert!(heard.lock().unwrap().is_empty());
        assert!(source.calls.lock().unwrap().1.is_empty());
    }

    #[test]
    fn nothing_starts_while_an_install_is_going() {
        let source = Arc::new(Scripted::new([found("0.2.0")].into()));
        *source.install.lock().unwrap() = (vec![Progress::Installing], Ok(()));
        let (updater, _) = updater(&source);
        updater.check();
        updater.install();
        assert_eq!(updater.status().state, Phase::Installing);
        // The script has no second check: asking the source would panic.
        assert_eq!(updater.check().state, Phase::Installing);
        updater.install();
        assert_eq!(source.calls.lock().unwrap().0, 1);
        assert_eq!(source.calls.lock().unwrap().1.len(), 1);
    }

    #[test]
    fn a_new_check_drops_the_update_it_replaces() {
        let source = Arc::new(Scripted::new([found("0.2.0"), Ok(None)].into()));
        let (updater, _) = updater(&source);
        updater.check();
        assert_eq!(updater.check().state, Phase::UpToDate);
        updater.install();
        assert!(source.calls.lock().unwrap().1.is_empty());
    }

    #[test]
    fn progress_is_a_fraction_when_the_size_is_known() {
        assert_eq!(fraction(25, Some(100)), Some(0.25));
        assert_eq!(fraction(150, Some(100)), Some(1.0));
        assert_eq!(fraction(10, Some(0)), None);
        assert_eq!(fraction(10, None), None);
    }

    #[test]
    fn progress_is_announced_about_ten_times_a_second() {
        let mut gap = Throttle::default();
        assert!(gap.allow(0, false), "the first one goes out");
        assert!(!gap.allow(40, false));
        assert!(!gap.allow(99, false));
        assert!(gap.allow(100, false));
        assert!(!gap.allow(150, false));
        assert!(gap.allow(160, true), "the end is never held back");
        assert!(!gap.allow(170, false));
        assert!(gap.allow(260, false));
    }
}
