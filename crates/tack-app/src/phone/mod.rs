//! The board on the user's phone (or any of their devices), over NetBird.
//! With the switch on, Tack serves a small page on this PC's NetBird
//! address, `http://<this-pc>.netbird.cloud:7717`. A phone with the NetBird
//! app connected opens it, the PC asks "Let pixel use your board?", and once
//! allowed the phone sees the board and can pin photos and text onto it,
//! from any network.
//!
//! - `mod.rs` (here): the switch, following NetBird as it connects and
//!   disconnects, who may get in, and the state the window shows.
//! - [`server`]: the HTTP side and its checks.
//! - [`window`]: the "Tack on your phone" window.
//!
//! Who gets in, in layers (docs/privacy.md has the long version):
//! 1. NetBird's access policies decide which devices can reach this PC at
//!    all; NetBird drops everything else before Tack sees it.
//! 2. Tack listens on the NetBird address only: never on the Wi-Fi, office
//!    network or internet.
//! 3. It answers only peers NetBird lists, and knows each by its WireGuard
//!    key ([`tack_core::phone`]): nothing of the board reaches a device the
//!    user has not allowed, on this PC, by name.
//! 4. The page refuses other websites (Host, Origin, Fetch Metadata) and
//!    limits what a device may send ([`server`]).

pub mod server;
pub mod window;

use std::sync::{Condvar, Mutex, MutexGuard};
use std::time::Duration;

use serde::Serialize;
use tack_core::history;
use tack_core::netbird::{Peer, Status};
use tack_core::phone::{Asking, Device, Gate, Verdict};
use tack_windows::netbird;
use tauri::AppHandle;

use crate::ipc::events;
use crate::state::lock;

/// The port the board is served on, on the NetBird address.
pub const PORT: u16 = 7717;

/// Where "Get NetBird" goes: NetBird's own install page, for every platform.
pub const NETBIRD_INSTALL: &str = "https://docs.netbird.io/get-started/install";

/// How often NetBird is looked at while the switch is on: it may have
/// connected, disconnected or given this PC another address.
const WATCH_EVERY: Duration = Duration::from_secs(20);

/// NetBird on this PC, as the window describes it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum NetBirdState {
    Missing,
    Disconnected,
    Connected,
}

/// What the "Tack on your phone" window shows (docs/ipc.md).
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PhoneState {
    on: bool,
    netbird: NetBirdState,
    serving: bool,
    address: Option<String>,
    qr: Option<String>,
    error: Option<String>,
    pc: Option<String>,
    devices: Vec<Device>,
    pending: Vec<Asking>,
}

struct Phone {
    gate: Gate,
    running: Option<server::Running>,
    netbird: NetBirdState,
    error: Option<String>,
}

static PHONE: Mutex<Phone> =
    Mutex::new(Phone { gate: Gate::new(), running: None, netbird: NetBirdState::Missing, error: None });

/// Wakes the watcher early (the switch was flipped).
static NUDGE: (Mutex<bool>, Condvar) = (Mutex::new(false), Condvar::new());

fn phone() -> MutexGuard<'static, Phone> {
    PHONE.lock().unwrap_or_else(|p| p.into_inner())
}

/// Starts watching NetBird, so the board is served whenever the switch is on
/// and NetBird is connected (from startup, if it was left on).
pub fn start(app: AppHandle) {
    let spawned = std::thread::Builder::new().name("tack-phone-watch".into()).spawn(move || loop {
        reconcile(&app);
        let (lock, wake) = &NUDGE;
        let nudged = lock.lock().unwrap_or_else(|p| p.into_inner());
        let (mut nudged, _) = wake.wait_timeout_while(nudged, WATCH_EVERY, |n| !*n).unwrap_or_else(|p| p.into_inner());
        *nudged = false;
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot watch NetBird for the phone board: {e}");
    }
}

fn nudge() {
    let (lock, wake) = &NUDGE;
    *lock.lock().unwrap_or_else(|p| p.into_inner()) = true;
    wake.notify_one();
}

/// Serves or stops serving to match the switch and NetBird. NetBird is only
/// asked while the switch is on.
fn reconcile(app: &AppHandle) {
    let on = lock(app).settings.phone.on;
    let status = if on { netbird::status() } else { None };
    let netbird_now = match &status {
        Some(_) => NetBirdState::Connected,
        None if on && netbird::installed() => NetBirdState::Disconnected,
        None if on => NetBirdState::Missing,
        None => phone().netbird,
    };

    let mut changed = false;
    {
        let mut p = phone();
        changed |= p.netbird != netbird_now;
        p.netbird = netbird_now;
        let keep = match (&p.running, &status) {
            (Some(running), Some(status)) => running.ip() == status.ip,
            _ => false,
        };
        if p.running.is_some() && !keep {
            if let Some(running) = p.running.take() {
                running.stop();
            }
            p.gate.clear();
            changed = true;
        }
        if let (Some(running), Some(status)) = (&p.running, &status) {
            running.update_peers(status.clone());
        }
    }
    if let Some(status) = status.filter(|_| phone().running.is_none()) {
        let started = server::start(app, status);
        let mut p = phone();
        match started {
            Ok(running) => {
                trace!("phone: serving {}", running.address());
                p.running = Some(running);
                p.error = None;
            }
            Err(e) => {
                eprintln!("tack: cannot serve the phone board: {e}");
                p.error = Some(format!("Couldn't start: {e}"));
            }
        }
        changed = true;
    }
    if !on && phone().error.take().is_some() {
        changed = true;
    }
    if changed {
        events::phone_state(app, &state_now(app));
    }
}

/// The window's state. Looks NetBird up when the switch is off, so the
/// window can say what turning it on would need.
pub fn state(app: &AppHandle) -> PhoneState {
    if !lock(app).settings.phone.on {
        phone().netbird = match netbird::status() {
            Some(_) => NetBirdState::Connected,
            None if netbird::installed() => NetBirdState::Disconnected,
            None => NetBirdState::Missing,
        };
    }
    state_now(app)
}

fn state_now(app: &AppHandle) -> PhoneState {
    let (on, devices) = {
        let s = lock(app);
        (s.settings.phone.on, s.settings.phone.devices.clone())
    };
    let mut p = phone();
    p.gate.lapse(history::now_ms());
    let address = p.running.as_ref().map(|r| r.address().to_string());
    PhoneState {
        on,
        netbird: p.netbird,
        serving: p.running.is_some(),
        qr: address.as_deref().and_then(qr_svg),
        address,
        error: p.error.clone(),
        pc: p.running.as_ref().map(|r| r.pc().to_string()),
        devices,
        pending: p.gate.pending().to_vec(),
    }
}

/// The switch.
pub fn set_on(app: &AppHandle, on: bool) -> PhoneState {
    {
        let mut s = lock(app);
        s.settings.phone.on = on;
        s.save();
    }
    reconcile(app);
    nudge();
    state(app)
}

/// The user's answer to "Let pixel use your board?".
pub fn answer(app: &AppHandle, key: &str, allow: bool) -> PhoneState {
    let now = history::now_ms();
    let name = phone().gate.answer(key, allow, now);
    if let (Some(name), true) = (name, allow) {
        let mut s = lock(app);
        s.settings.phone.allow(key, &name, now);
        s.save();
    }
    let state = state_now(app);
    events::phone_state(app, &state);
    state
}

/// Removes an allowed device: from its next request on, it has to ask.
pub fn forget(app: &AppHandle, key: &str) -> PhoneState {
    {
        let mut s = lock(app);
        if s.settings.phone.forget(key) {
            s.save();
        }
    }
    let state = state_now(app);
    events::phone_state(app, &state);
    state
}

/// Whether `peer` may use the board; a device asking for the first time
/// brings the window up with the question.
pub(crate) fn admit(app: &AppHandle, peer: &Peer) -> Verdict {
    decide(app, |gate, settings, now| gate.check(settings, &peer.key, peer.short_name(), now))
}

/// A turned-down device asks again ("Ask again" on the phone).
pub(crate) fn ask_again(app: &AppHandle, peer: &Peer) -> Verdict {
    decide(app, |gate, settings, now| gate.ask_again(settings, &peer.key, peer.short_name(), now))
}

fn decide(app: &AppHandle, rule: impl FnOnce(&mut Gate, &tack_core::phone::PhoneSettings, u64) -> Verdict) -> Verdict {
    let settings = lock(app).settings.phone.clone();
    let verdict = rule(&mut phone().gate, &settings, history::now_ms());
    if verdict == (Verdict::Waiting { asked: true }) {
        trace!("phone: a device asks to use the board");
        events::phone_state(app, &state_now(app));
        window::open(app);
    }
    verdict
}

/// A QR code for `address`, as SVG markup for the window.
fn qr_svg(address: &str) -> Option<String> {
    let code = qrcode::QrCode::new(address.as_bytes()).ok()?;
    Some(
        code.render::<qrcode::render::svg::Color>()
            .min_dimensions(176, 176)
            .dark_color(qrcode::render::svg::Color("#000000"))
            .light_color(qrcode::render::svg::Color("#ffffff"))
            .quiet_zone(true)
            .build(),
    )
}

/// This PC's short name on NetBird ("my-pc").
fn short(status: &Status) -> &str {
    status.fqdn.split('.').next().unwrap_or(&status.fqdn)
}
