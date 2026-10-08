//! The phone board's listener, on this PC's NetBird address only. A
//! connection from anything NetBird does not list as a peer is closed before
//! a byte is read; WireGuard binds each peer's address to its key, so the
//! address really is that peer. Connections are capped, every read and write
//! has a timeout ([`super::http`]), and the routes are in [`super::routes`].
//!
//! Plain HTTP: the WireGuard tunnel encrypts it end to end, and NetBird gives
//! peers no certificates (so the phone's browser says "Not secure").

use std::collections::{HashMap, VecDeque};
use std::net::{IpAddr, Ipv4Addr, Shutdown, SocketAddr, TcpListener, TcpStream};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use tack_core::netbird::{Peer, Status};
use tack_windows::netbird;
use tauri::AppHandle;

use super::http::{self, Response};
use super::{routes, PORT};

/// Connections open at once; more are closed at once. A phone uses a few.
const MAX_CONNECTIONS: usize = 16;
/// Pins being handled at once, and per device per minute.
const MAX_PINNING: usize = 2;
const PINS_PER_MINUTE: usize = 30;
/// How often an unknown address may make Tack ask NetBird again (a peer
/// that joined since the last look).
const UNKNOWN_RECHECK: Duration = Duration::from_secs(3);
/// How old NetBird's peer list may be when it decides an API request: an
/// address freed by one peer and given to another is seen within this.
const FRESH_FOR: Duration = Duration::from_secs(10);

/// The board being served.
pub struct Running {
    stop: Arc<AtomicBool>,
    shared: Arc<Shared>,
    ip: Ipv4Addr,
    address: String,
    pc: String,
}

/// What the connection threads share.
pub(super) struct Shared {
    /// NetBird's view of this PC and its peers, and when it was looked up.
    status: Mutex<(Status, Instant)>,
    refreshing: AtomicBool,
    connections: AtomicUsize,
    pinning: AtomicUsize,
    /// Recent pins per device key, for the rate limit.
    pins: Mutex<HashMap<String, VecDeque<Instant>>>,
}

impl Running {
    pub fn ip(&self) -> Ipv4Addr {
        self.ip
    }

    pub fn address(&self) -> &str {
        &self.address
    }

    pub fn pc(&self) -> &str {
        &self.pc
    }

    /// A fresh look at NetBird's peers (from the watcher).
    pub fn update_peers(&self, status: Status) {
        if status.ip == self.ip {
            *self.shared.status.lock().unwrap_or_else(|p| p.into_inner()) = (status, Instant::now());
        }
    }

    /// Stops serving; the port closes.
    pub fn stop(self) {
        self.stop.store(true, Ordering::SeqCst);
        // Wakes the accept loop so it sees the flag and drops the listener.
        let _ = TcpStream::connect_timeout(&SocketAddr::new(IpAddr::V4(self.ip), PORT), Duration::from_secs(1));
        trace!("phone: stopped serving {}", self.address);
    }
}

/// Starts serving on `status`'s NetBird address.
pub fn start(app: &AppHandle, status: Status) -> Result<Running, String> {
    let ip = status.ip;
    let listener = TcpListener::bind(SocketAddr::new(IpAddr::V4(ip), PORT)).map_err(|e| match e.kind() {
        std::io::ErrorKind::AddrInUse => format!("port {PORT} is in use"),
        _ => e.to_string(),
    })?;
    let address = format!("http://{}:{PORT}", status.fqdn);
    let pc = super::short(&status).to_string();
    let shared = Arc::new(Shared {
        status: Mutex::new((status, Instant::now())),
        refreshing: AtomicBool::new(false),
        connections: AtomicUsize::new(0),
        pinning: AtomicUsize::new(0),
        pins: Mutex::new(HashMap::new()),
    });
    let stop = Arc::new(AtomicBool::new(false));
    let (app, accepting, stopping) = (app.clone(), shared.clone(), stop.clone());
    std::thread::Builder::new()
        .name("tack-phone".into())
        .spawn(move || {
            for conn in listener.incoming() {
                if stopping.load(Ordering::SeqCst) {
                    break;
                }
                let Ok(stream) = conn else { continue };
                if accepting.connections.fetch_add(1, Ordering::SeqCst) >= MAX_CONNECTIONS {
                    accepting.connections.fetch_sub(1, Ordering::SeqCst);
                    continue;
                }
                let (app, shared) = (app.clone(), accepting.clone());
                let spawned = std::thread::Builder::new().name("tack-phone-conn".into()).spawn(move || {
                    serve(&app, &shared, stream);
                    shared.connections.fetch_sub(1, Ordering::SeqCst);
                });
                if spawned.is_err() {
                    accepting.connections.fetch_sub(1, Ordering::SeqCst);
                }
            }
        })
        .map_err(|e| e.to_string())?;
    Ok(Running { stop, shared, ip, address, pc })
}

/// One request on one connection, then the connection closes.
fn serve(app: &AppHandle, shared: &Shared, mut stream: TcpStream) {
    let Ok(SocketAddr::V4(from)) = stream.peer_addr() else { return };
    let Some((me, peer)) = shared.peer(*from.ip(), false) else {
        trace!("phone: closed a connection from {}, not a NetBird peer", from.ip());
        return;
    };
    let _ = stream.set_nodelay(true);
    let response = match http::read_head(&mut stream) {
        Ok(mut head) => routes::handle(app, shared, &mut stream, &mut head, &me, &peer),
        Err(e) => match e.status() {
            Some(code) => Response::empty(code),
            None => return,
        },
    };
    let _ = response.send(&mut stream, routes::CSP);
    let _ = stream.shutdown(Shutdown::Both);
}

impl Shared {
    /// This PC's status and the peer at `ip`, if NetBird lists one there.
    /// `fresh` asks for a peer list no older than [`FRESH_FOR`]. NetBird is
    /// asked by one thread at a time, without holding the lock.
    pub(super) fn peer(&self, ip: Ipv4Addr, fresh: bool) -> Option<(Status, Peer)> {
        let (known, age) = {
            let g = self.status.lock().unwrap_or_else(|p| p.into_inner());
            (g.0.peer(ip).is_some(), g.1.elapsed())
        };
        let stale = if known { fresh && age >= FRESH_FOR } else { age >= UNKNOWN_RECHECK };
        if stale && !self.refreshing.swap(true, Ordering::SeqCst) {
            let looked = netbird::status();
            let mut g = self.status.lock().unwrap_or_else(|p| p.into_inner());
            g.1 = Instant::now();
            if let Some(status) = looked.filter(|s| s.ip == g.0.ip) {
                g.0 = status;
            }
            drop(g);
            self.refreshing.store(false, Ordering::SeqCst);
        }
        let g = self.status.lock().unwrap_or_else(|p| p.into_inner());
        let peer = g.0.peer(ip)?.clone();
        Some((g.0.clone(), peer))
    }

    /// Counts a pin from `key`; false when it is over the per-minute limit.
    pub(super) fn may_pin(&self, key: &str) -> bool {
        let mut pins = self.pins.lock().unwrap_or_else(|p| p.into_inner());
        let now = Instant::now();
        let recent = pins.entry(key.to_string()).or_default();
        while recent.front().is_some_and(|t| now.duration_since(*t) >= Duration::from_secs(60)) {
            recent.pop_front();
        }
        if recent.len() >= PINS_PER_MINUTE {
            return false;
        }
        recent.push_back(now);
        true
    }

    /// A turn to pin, if fewer than [`MAX_PINNING`] are under way.
    pub(super) fn pin_turn(&self) -> Option<PinTurn<'_>> {
        if self.pinning.fetch_add(1, Ordering::SeqCst) >= MAX_PINNING {
            self.pinning.fetch_sub(1, Ordering::SeqCst);
            return None;
        }
        Some(PinTurn(&self.pinning))
    }
}

/// Holds one of the pin turns until dropped.
pub(super) struct PinTurn<'a>(&'a AtomicUsize);

impl Drop for PinTurn<'_> {
    fn drop(&mut self) {
        self.0.fetch_sub(1, Ordering::SeqCst);
    }
}
