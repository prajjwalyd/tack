//! Who may use the board from another device (the phone board, served over
//! NetBird). The rules, with no networking in them:
//!
//! - A device is known by its NetBird WireGuard public key, never by its
//!   address or name: addresses can move between peers and names can look
//!   alike, keys cannot.
//! - A device the user allowed gets in, until the user removes it.
//! - Any other device is asked about once. The PC shows its name, address
//!   and a code, and the device's page shows the same code, so the user can
//!   tell their phone from a look-alike. Nothing of the board reaches it
//!   until the user clicks Allow.
//! - A device the user turned down stays out. It may ask again after
//!   [`ASK_AGAIN_AFTER_MS`], twice as long after each further refusal.
//! - At most [`MAX_PENDING`] devices wait at once; a request nobody answers
//!   lapses after [`PENDING_FOR_MS`].

use serde::{Deserialize, Serialize};

/// How many devices may wait for an answer at once. More than a person has
/// at hand; anything past it is turned away rather than queued.
pub const MAX_PENDING: usize = 3;
/// How long a request waits for the user before it lapses.
pub const PENDING_FOR_MS: u64 = 10 * 60 * 1000;
/// How soon a device that was turned down may ask again; doubled after each
/// further refusal, up to [`ASK_AGAIN_MAX_MS`].
pub const ASK_AGAIN_AFTER_MS: u64 = 30 * 1000;
pub const ASK_AGAIN_MAX_MS: u64 = 60 * 60 * 1000;

/// The phone board's saved settings.
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct PhoneSettings {
    /// The user's switch: serve the board to their devices.
    pub on: bool,
    /// The devices the user allowed.
    pub devices: Vec<Device>,
}

/// A device the user allowed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Device {
    /// Its NetBird WireGuard public key.
    pub key: String,
    /// Its NetBird name when it was allowed ("pixel"), for the list.
    pub name: String,
    /// When it was allowed, ms since the Unix epoch.
    pub approved_at: u64,
}

impl PhoneSettings {
    pub fn allows(&self, key: &str) -> bool {
        self.devices.iter().any(|d| d.key == key)
    }

    /// Allows a device (again: its name and date are updated).
    pub fn allow(&mut self, key: &str, name: &str, now_ms: u64) {
        self.devices.retain(|d| d.key != key);
        self.devices.push(Device { key: key.into(), name: name.into(), approved_at: now_ms });
    }

    /// Removes a device; it has to ask again.
    pub fn forget(&mut self, key: &str) -> bool {
        let before = self.devices.len();
        self.devices.retain(|d| d.key != key);
        self.devices.len() != before
    }
}

/// A device asking to use the board, as NetBird knows it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Who {
    pub key: String,
    /// Its short NetBird name ("pixel").
    pub name: String,
    /// Its full NetBird name ("pixel.netbird.cloud").
    pub fqdn: String,
    /// Its NetBird address.
    pub ip: String,
}

/// A device waiting for the user's answer.
#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Asking {
    pub key: String,
    pub name: String,
    pub fqdn: String,
    pub ip: String,
    /// Shown on the PC and on the device's own page, to compare.
    pub code: String,
    #[serde(skip)]
    pub since_ms: u64,
}

/// What a device gets.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Verdict {
    /// It was allowed: the board is open to it.
    Allowed,
    /// It waits for the user, with the code to compare. `asked` is true when
    /// this request put the question to the user, so the PC should show it.
    Waiting { asked: bool, code: String },
    /// The user turned it down.
    Refused,
    /// Too many devices are already waiting.
    Busy,
}

/// A device the user turned down: when, how often, and whether it has since
/// been let ask again.
#[derive(Debug)]
struct Refusal {
    key: String,
    at_ms: u64,
    times: u32,
    lifted: bool,
}

impl Refusal {
    fn may_ask_again(&self, now_ms: u64) -> bool {
        let wait = ASK_AGAIN_AFTER_MS.saturating_mul(1 << self.times.saturating_sub(1).min(16)).min(ASK_AGAIN_MAX_MS);
        now_ms.saturating_sub(self.at_ms) >= wait
    }
}

/// The devices waiting for an answer, and those turned down, while Tack
/// runs. Refusals outlive the server stopping and starting again; only a
/// restart of Tack forgets them.
#[derive(Debug, Default)]
pub struct Gate {
    pending: Vec<Asking>,
    refused: Vec<Refusal>,
}

impl Gate {
    pub const fn new() -> Gate {
        Gate { pending: Vec::new(), refused: Vec::new() }
    }

    /// The devices waiting, oldest first.
    pub fn pending(&self) -> &[Asking] {
        &self.pending
    }

    /// Whether `key` may use the board, without asking anybody: for
    /// requests that must not raise the question (see the server).
    pub fn allowed(settings: &PhoneSettings, key: &str) -> bool {
        settings.allows(key)
    }

    /// A request from `who`: whether it gets in, and whether it needs asking
    /// about. `code` is a fresh random code, used if this request asks.
    pub fn check(&mut self, settings: &PhoneSettings, who: &Who, code: &str, now_ms: u64) -> Verdict {
        self.lapse(now_ms);
        if settings.allows(&who.key) {
            return Verdict::Allowed;
        }
        if self.refused.iter().any(|r| r.key == who.key && !r.lifted) {
            return Verdict::Refused;
        }
        if let Some(asking) = self.pending.iter().find(|a| a.key == who.key) {
            return Verdict::Waiting { asked: false, code: asking.code.clone() };
        }
        if self.pending.len() >= MAX_PENDING {
            return Verdict::Busy;
        }
        self.pending.push(Asking {
            key: who.key.clone(),
            name: who.name.clone(),
            fqdn: who.fqdn.clone(),
            ip: who.ip.clone(),
            code: code.into(),
            since_ms: now_ms,
        });
        Verdict::Waiting { asked: true, code: code.into() }
    }

    /// A turned-down device asks again ("Ask again" on its page).
    pub fn ask_again(&mut self, settings: &PhoneSettings, who: &Who, code: &str, now_ms: u64) -> Verdict {
        if let Some(r) = self.refused.iter_mut().find(|r| r.key == who.key && !r.lifted) {
            if !r.may_ask_again(now_ms) {
                return Verdict::Refused;
            }
            r.lifted = true;
        }
        self.check(settings, who, code, now_ms)
    }

    /// The user's answer about a waiting device. Returns its name if it was
    /// waiting; when `allow`, the caller saves it in the settings.
    pub fn answer(&mut self, key: &str, allow: bool, now_ms: u64) -> Option<String> {
        self.lapse(now_ms);
        let i = self.pending.iter().position(|a| a.key == key)?;
        let asking = self.pending.remove(i);
        if !allow {
            match self.refused.iter_mut().find(|r| r.key == key) {
                Some(r) => {
                    r.times += 1;
                    r.at_ms = now_ms;
                    r.lifted = false;
                }
                None => self.refused.push(Refusal { key: key.into(), at_ms: now_ms, times: 1, lifted: false }),
            }
        }
        Some(asking.name)
    }

    /// Forgets the waiting requests (the board stopped being served).
    pub fn clear(&mut self) {
        self.pending.clear();
    }

    /// Lets requests nobody answered in time go.
    pub fn lapse(&mut self, now_ms: u64) {
        self.pending.retain(|a| now_ms.saturating_sub(a.since_ms) < PENDING_FOR_MS);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn who(key: &str, name: &str) -> Who {
        Who { key: key.into(), name: name.into(), fqdn: format!("{name}.netbird.cloud"), ip: "100.90.1.2".into() }
    }

    fn waiting(asked: bool, code: &str) -> Verdict {
        Verdict::Waiting { asked, code: code.into() }
    }

    const PIXEL: &str = "cGl4ZWw=";
    const LAPTOP: &str = "bGFwdG9w";

    #[test]
    fn a_new_device_is_asked_about_once_with_one_code() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        assert_eq!(gate.check(&settings, &who(PIXEL, "pixel"), "1234", 0), waiting(true, "1234"));
        assert_eq!(gate.check(&settings, &who(PIXEL, "pixel"), "9999", 1_000), waiting(false, "1234"));
        assert_eq!(gate.pending().len(), 1);
        assert_eq!(gate.pending()[0].fqdn, "pixel.netbird.cloud");
    }

    #[test]
    fn allowing_lets_the_device_in_by_its_key() {
        let (mut gate, mut settings) = (Gate::new(), PhoneSettings::default());
        gate.check(&settings, &who(PIXEL, "pixel"), "1234", 0);
        assert_eq!(gate.answer(PIXEL, true, 5).as_deref(), Some("pixel"));
        settings.allow(PIXEL, "pixel", 5);
        assert_eq!(gate.check(&settings, &who(PIXEL, "renamed"), "0000", 10), Verdict::Allowed);
        assert!(Gate::allowed(&settings, PIXEL));
        assert_eq!(
            gate.check(&settings, &who(LAPTOP, "pixel"), "5678", 10),
            waiting(true, "5678"),
            "a name is not an identity"
        );
    }

    #[test]
    fn each_refusal_makes_the_next_ask_wait_longer() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        let pixel = who(PIXEL, "pixel");
        gate.check(&settings, &pixel, "1", 0);
        gate.answer(PIXEL, false, 0);
        assert_eq!(gate.check(&settings, &pixel, "2", 1_000), Verdict::Refused);
        assert_eq!(gate.ask_again(&settings, &pixel, "2", 1_000), Verdict::Refused);
        assert_eq!(gate.ask_again(&settings, &pixel, "3", ASK_AGAIN_AFTER_MS), waiting(true, "3"));

        let t = ASK_AGAIN_AFTER_MS;
        gate.answer(PIXEL, false, t);
        assert_eq!(
            gate.ask_again(&settings, &pixel, "4", t + ASK_AGAIN_AFTER_MS),
            Verdict::Refused,
            "twice as long now"
        );
        assert_eq!(gate.ask_again(&settings, &pixel, "5", t + 2 * ASK_AGAIN_AFTER_MS), waiting(true, "5"));
    }

    #[test]
    fn refusals_outlive_the_server_stopping() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        gate.check(&settings, &who(PIXEL, "pixel"), "1", 0);
        gate.answer(PIXEL, false, 0);
        gate.clear();
        assert_eq!(gate.check(&settings, &who(PIXEL, "pixel"), "2", 1), Verdict::Refused);
    }

    #[test]
    fn only_a_few_devices_may_wait_at_once() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        for n in 0..MAX_PENDING {
            assert!(matches!(
                gate.check(&settings, &who(&format!("k{n}"), "x"), "1", 0),
                Verdict::Waiting { asked: true, .. }
            ));
        }
        assert_eq!(gate.check(&settings, &who("one-more", "x"), "1", 0), Verdict::Busy);
    }

    #[test]
    fn an_unanswered_request_lapses_and_can_no_longer_be_allowed() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        gate.check(&settings, &who(PIXEL, "pixel"), "1", 0);
        assert_eq!(gate.answer(PIXEL, true, PENDING_FOR_MS), None);
        assert!(gate.pending().is_empty());
    }

    #[test]
    fn a_removed_device_has_to_ask_again() {
        let (mut gate, mut settings) = (Gate::new(), PhoneSettings::default());
        settings.allow(PIXEL, "pixel", 0);
        assert!(settings.forget(PIXEL));
        assert!(!Gate::allowed(&settings, PIXEL));
        assert_eq!(gate.check(&settings, &who(PIXEL, "pixel"), "1", 1), waiting(true, "1"));
    }

    #[test]
    fn answering_a_device_that_is_not_waiting_does_nothing() {
        let mut gate = Gate::new();
        assert_eq!(gate.answer(PIXEL, true, 0), None);
    }
}
