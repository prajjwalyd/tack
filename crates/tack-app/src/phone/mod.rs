//! The board on the user's phone (or any of their devices), over NetBird.
//! With the switch on, Tack serves a small page on this PC's NetBird
//! address; a device that opens it asks "Let pixel use your board?", and
//! once allowed it sees the board and can pin photos and text onto it.
//! This module keeps the server in step with the switch and NetBird, and
//! holds who may get in.
//!
//! Who gets in, in layers (docs/privacy.md has the long version):
//! 1. NetBird's access policies decide which devices can reach this PC at
//!    all.
//! 2. Tack listens on the NetBird address only, never on the Wi-Fi, office
//!    network or internet.
//! 3. It answers only peers NetBird lists, and knows each by its WireGuard
//!    key ([`tack_core::phone`]): nothing of the board reaches a device the
//!    user has not allowed, on this PC, by name.
//! 4. The page refuses other websites and limits what a device may send
//!    ([`routes`]); the server has hard limits of its own ([`http`]).

mod http;
mod routes;
pub mod server;
pub mod window;

use std::collections::hash_map::RandomState;
use std::hash::BuildHasher;
use std::sync::{Condvar, Mutex, MutexGuard};
use std::time::{Duration, SystemTime};

use serde::Serialize;
use tack_core::history;
use tack_core::netbird::{Peer, Status};
use tack_core::phone::{Asking, Device, Gate, PhoneSettings, Verdict, Who};
use tack_windows::netbird;
use tauri::AppHandle;

use crate::ipc::events;
use crate::state::lock;

/// The port the board is served on, on the NetBird address.
pub const PORT: u16 = 7717;

/// Where "Get NetBird" goes: NetBird's own install page, for every platform.
pub const NETBIRD_INSTALL: &str = "https://docs.netbird.io/get-started/install";

/// How often NetBird is looked at while the switch is on: it may have
/// connected, disconnected or given this PC another address. Less often once
/// serving, since requests look up the peers they need themselves.
const WATCH_WAITING: Duration = Duration::from_secs(15);
const WATCH_SERVING: Duration = Duration::from_secs(60);

/// NetBird on this PC, as the window describes it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum NetBirdState {
    Missing,
    Disconnected,
    Connected,
}

/// What the "Tack on your phone" window shows.
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
/// One reconcile at a time, so two cannot both start a server.
static RECONCILING: Mutex<()> = Mutex::new(());

fn phone() -> MutexGuard<'static, Phone> {
    PHONE.lock().unwrap_or_else(|p| p.into_inner())
}

/// Starts watching NetBird, so the board is served whenever the switch is on
/// and NetBird is connected (from startup, if it was left on).
pub fn start(app: AppHandle) {
    let spawned = std::thread::Builder::new().name("tack-phone-watch".into()).spawn(move || loop {
        reconcile(&app);
        let every = if phone().running.is_some() { WATCH_SERVING } else { WATCH_WAITING };
        let (lock, wake) = &NUDGE;
        let nudged = lock.lock().unwrap_or_else(|p| p.into_inner());
        let (mut nudged, _) = wake.wait_timeout_while(nudged, every, |n| !*n).unwrap_or_else(|p| p.into_inner());
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
    let _one = RECONCILING.lock().unwrap_or_else(|p| p.into_inner());
    let on = lock(app).settings.phone.on;
    let status = if on { netbird::status() } else { None };
    let netbird_now = if on { netbird_state(status.as_ref()) } else { phone().netbird };

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
        phone().netbird = netbird_state(netbird::status().as_ref());
    }
    state_now(app)
}

fn netbird_state(status: Option<&Status>) -> NetBirdState {
    match status {
        Some(_) => NetBirdState::Connected,
        None if netbird::installed() => NetBirdState::Disconnected,
        None => NetBirdState::Missing,
    }
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

/// Whether the device with `key` is allowed, without asking anybody.
pub(crate) fn allowed(app: &AppHandle, key: &str) -> bool {
    Gate::allowed(&lock(app).settings.phone, key)
}

/// Whether `peer` may use the board; a device asking for the first time
/// puts the question to the user.
pub(crate) fn admit(app: &AppHandle, peer: &Peer) -> Verdict {
    decide(app, peer, |gate, settings, who, code, now| gate.check(settings, who, code, now))
}

/// A turned-down device asks again ("Ask again" on its page).
pub(crate) fn ask_again(app: &AppHandle, peer: &Peer) -> Verdict {
    decide(app, peer, |gate, settings, who, code, now| gate.ask_again(settings, who, code, now))
}

type Rule = fn(&mut Gate, &PhoneSettings, &Who, &str, u64) -> Verdict;

fn decide(app: &AppHandle, peer: &Peer, rule: Rule) -> Verdict {
    let settings = lock(app).settings.phone.clone();
    let who = Who {
        key: peer.key.clone(),
        name: peer.short_name().to_string(),
        fqdn: peer.fqdn.clone(),
        ip: peer.ip.to_string(),
    };
    let verdict = rule(&mut phone().gate, &settings, &who, &fresh_code(), history::now_ms());
    if matches!(verdict, Verdict::Waiting { asked: true, .. }) {
        trace!("phone: a device asks to use the board");
        events::phone_state(app, &state_now(app));
        window::ask(app);
    }
    verdict
}

/// A four-digit code for one request, shown on the PC and on the device.
/// Only has to differ between requests, not be secret: what it proves is
/// that the prompt on the PC belongs to the phone in your hand.
fn fresh_code() -> String {
    format!("{:04}", RandomState::new().hash_one(SystemTime::now()) % 10_000)
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
