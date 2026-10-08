//! Who may use the board from another device (the phone board, served over
//! NetBird). The rules, with no networking in them:
//!
//! - A device is known by its NetBird WireGuard public key, never by its
//!   address: addresses can move between peers, keys cannot, and NetBird
//!   only delivers a peer's packets from the address bound to its key.
//! - A device the user allowed gets in. Allowed devices are saved
//!   (`Settings::phone`) until the user removes them.
//! - Any other device is asked about once: it waits while the PC shows
//!   "Let pixel use your board?", and nothing of the board reaches it until
//!   the user clicks Allow.
//! - A device the user turned down stays out. It can ask again, but not
//!   more than once every [`ASK_AGAIN_AFTER_MS`], so it cannot keep the
//!   prompt coming back.
//! - Requests are left unanswered past [`MAX_PENDING`] devices at once, and
//!   a request that nobody answered lapses after [`PENDING_FOR_MS`].

use serde::{Deserialize, Serialize};

/// How many devices may wait for an answer at once. More than a person has
/// at hand; anything past it is turned away rather than queued.
pub const MAX_PENDING: usize = 3;
/// How long a request waits for the user before it lapses.
pub const PENDING_FOR_MS: u64 = 10 * 60 * 1000;
/// How soon a device that was turned down may ask again.
pub const ASK_AGAIN_AFTER_MS: u64 = 30 * 1000;

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

/// A device waiting for the user's answer.
#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Asking {
    pub key: String,
    pub name: String,
    #[serde(skip)]
    pub since_ms: u64,
}

/// What a device gets.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Verdict {
    /// It was allowed: the board is open to it.
    Allowed,
    /// It waits for the user. `asked` is true when this request is what
    /// put the question to the user, so the PC should show it now.
    Waiting { asked: bool },
    /// It was turned down, or too many devices are already waiting.
    Refused,
}

/// The devices waiting for an answer, and those turned down, while Tack
/// runs. (Refusals are not saved: after a restart a device may ask once
/// more.)
#[derive(Debug, Default)]
pub struct Gate {
    pending: Vec<Asking>,
    refused: Vec<(String, u64)>,
}

impl Gate {
    pub const fn new() -> Gate {
        Gate { pending: Vec::new(), refused: Vec::new() }
    }

    /// The devices waiting, oldest first.
    pub fn pending(&self) -> &[Asking] {
        &self.pending
    }

    /// A request from the device with `key`: whether it gets in, and whether
    /// it needs asking about.
    pub fn check(&mut self, settings: &PhoneSettings, key: &str, name: &str, now_ms: u64) -> Verdict {
        self.lapse(now_ms);
        if settings.allows(key) {
            return Verdict::Allowed;
        }
        if self.refused.iter().any(|(k, _)| k == key) {
            return Verdict::Refused;
        }
        if self.pending.iter().any(|a| a.key == key) {
            return Verdict::Waiting { asked: false };
        }
        if self.pending.len() >= MAX_PENDING {
            return Verdict::Refused;
        }
        self.pending.push(Asking { key: key.into(), name: name.into(), since_ms: now_ms });
        Verdict::Waiting { asked: true }
    }

    /// A turned-down device asks again ("Ask again" on the phone). Allowed
    /// once [`ASK_AGAIN_AFTER_MS`] have passed since the refusal.
    pub fn ask_again(&mut self, settings: &PhoneSettings, key: &str, name: &str, now_ms: u64) -> Verdict {
        if let Some(i) = self.refused.iter().position(|(k, _)| k == key) {
            if now_ms.saturating_sub(self.refused[i].1) < ASK_AGAIN_AFTER_MS {
                return Verdict::Refused;
            }
            self.refused.remove(i);
        }
        self.check(settings, key, name, now_ms)
    }

    /// The user's answer about a waiting device. Returns its name if it was
    /// waiting; when `allow`, the caller saves it in the settings.
    pub fn answer(&mut self, key: &str, allow: bool, now_ms: u64) -> Option<String> {
        self.lapse(now_ms);
        let i = self.pending.iter().position(|a| a.key == key)?;
        let asking = self.pending.remove(i);
        if !allow {
            self.refused.retain(|(k, _)| k != key);
            self.refused.push((key.into(), now_ms));
        }
        Some(asking.name)
    }

    /// Forgets every request and refusal (the board stopped being served).
    pub fn clear(&mut self) {
        self.pending.clear();
        self.refused.clear();
    }

    /// Lets requests nobody answered in time go.
    pub fn lapse(&mut self, now_ms: u64) {
        self.pending.retain(|a| now_ms.saturating_sub(a.since_ms) < PENDING_FOR_MS);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const PIXEL: &str = "cGl4ZWw=";
    const LAPTOP: &str = "bGFwdG9w";

    #[test]
    fn a_new_device_is_asked_about_once() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        assert_eq!(gate.check(&settings, PIXEL, "pixel", 0), Verdict::Waiting { asked: true });
        assert_eq!(gate.check(&settings, PIXEL, "pixel", 1_000), Verdict::Waiting { asked: false });
        assert_eq!(gate.pending().len(), 1);
    }

    #[test]
    fn allowing_lets_the_device_in_by_its_key() {
        let (mut gate, mut settings) = (Gate::new(), PhoneSettings::default());
        gate.check(&settings, PIXEL, "pixel", 0);
        assert_eq!(gate.answer(PIXEL, true, 5).as_deref(), Some("pixel"));
        settings.allow(PIXEL, "pixel", 5);
        assert_eq!(gate.check(&settings, PIXEL, "renamed", 10), Verdict::Allowed);
        assert_eq!(
            gate.check(&settings, LAPTOP, "pixel", 10),
            Verdict::Waiting { asked: true },
            "a name is not an identity"
        );
    }

    #[test]
    fn a_refused_device_stays_out_and_may_ask_again_only_later() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        gate.check(&settings, PIXEL, "pixel", 0);
        gate.answer(PIXEL, false, 1_000);
        assert_eq!(gate.check(&settings, PIXEL, "pixel", 2_000), Verdict::Refused);
        assert_eq!(gate.ask_again(&settings, PIXEL, "pixel", 2_000), Verdict::Refused);
        assert_eq!(
            gate.ask_again(&settings, PIXEL, "pixel", 1_000 + ASK_AGAIN_AFTER_MS),
            Verdict::Waiting { asked: true }
        );
    }

    #[test]
    fn only_a_few_devices_may_wait_at_once() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        for n in 0..MAX_PENDING {
            assert_eq!(gate.check(&settings, &format!("k{n}"), "x", 0), Verdict::Waiting { asked: true });
        }
        assert_eq!(gate.check(&settings, "one-more", "x", 0), Verdict::Refused);
    }

    #[test]
    fn an_unanswered_request_lapses() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        gate.check(&settings, PIXEL, "pixel", 0);
        assert_eq!(gate.check(&settings, LAPTOP, "laptop", PENDING_FOR_MS), Verdict::Waiting { asked: true });
        assert_eq!(gate.pending().len(), 1, "pixel's request lapsed");
    }

    #[test]
    fn a_removed_device_has_to_ask_again() {
        let (mut gate, mut settings) = (Gate::new(), PhoneSettings::default());
        settings.allow(PIXEL, "pixel", 0);
        assert!(settings.forget(PIXEL));
        assert_eq!(gate.check(&settings, PIXEL, "pixel", 1), Verdict::Waiting { asked: true });
    }

    #[test]
    fn a_lapsed_request_can_no_longer_be_allowed() {
        let (mut gate, settings) = (Gate::new(), PhoneSettings::default());
        gate.check(&settings, PIXEL, "pixel", 0);
        assert_eq!(gate.answer(PIXEL, true, PENDING_FOR_MS), None);
    }

    #[test]
    fn answering_a_device_that_is_not_waiting_does_nothing() {
        let mut gate = Gate::new();
        assert_eq!(gate.answer(PIXEL, true, 0), None);
    }
}
