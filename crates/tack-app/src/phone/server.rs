//! The phone board's HTTP side: a small server on this PC's NetBird address.
//! The page and its files go to any NetBird peer (they hold nothing of the
//! board); everything under `/api/` only to devices the user allowed
//! (`super::admit`).
//!
//! Checks on every request, before anything else:
//! - The sender is a peer NetBird lists. WireGuard binds each peer's address
//!   to its key, so the address really is that peer.
//! - Host names this PC, so a web page cannot point a name of its own at
//!   this address (DNS rebinding) and read the board through it.
//! - Fetch Metadata and Origin, when the browser sends them, say the request
//!   comes from this page itself, not from another site open on the phone.
//! - Only GET and POST; bodies have a size cap, a type allowlist and a rate
//!   limit per device.
//!
//! Plain HTTP: the WireGuard tunnel encrypts it end to end, and NetBird gives
//! peers no certificates (so the phone's browser says "Not secure").

use std::collections::{HashMap, VecDeque};
use std::io::Read;
use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde::Serialize;
use serde_json::json;
use tack_core::netbird::{Peer, Status};
use tack_core::phone::Verdict;
use tack_core::{note, Kind};
use tack_windows::netbird;
use tauri::AppHandle;
use tiny_http::{Header, Method, Request, Response, Server};

use super::PORT;
use crate::notes;
use crate::state::lock;

/// The largest body accepted: a phone photo, already scaled down by the page.
const MAX_BODY: usize = 40 * 1024 * 1024;
/// Requests handled at once; more get "busy".
const MAX_IN_FLIGHT: usize = 8;
/// Pins a device may send per minute.
const PINS_PER_MINUTE: usize = 30;
/// How often an unknown address may make Tack ask NetBird again (a peer
/// that joined since the last look).
const RECHECK: Duration = Duration::from_secs(3);

const PAGE: &str = include_str!("../../../../ui/phone/index.html");
const SCRIPT: &str = include_str!("../../../../ui/phone/phone.js");
const STYLE: &str = include_str!("../../../../ui/phone/phone.css");
const ICON: &[u8] = include_bytes!("../../icons/128x128@2x.png");
/// Lets Android's "Add to Home screen" use Tack's name and icon.
const MANIFEST: &str = r##"{"name":"Tack","short_name":"Tack","start_url":"/","display":"standalone","background_color":"#153328","theme_color":"#1c4234","icons":[{"src":"/icon.png","sizes":"256x256","type":"image/png"}]}"##;

/// The page may load only itself; thumbnails are data URLs, previews blobs.
const CSP: &str = concat!(
    "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; ",
    "connect-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
);

/// The board being served.
pub struct Running {
    server: Arc<Server>,
    shared: Arc<Shared>,
    address: String,
    pc: String,
}

/// What the request threads share.
struct Shared {
    /// NetBird's view: this PC and its peers, and when it was looked up.
    status: Mutex<(Status, Instant)>,
    in_flight: AtomicUsize,
    /// Recent pins per device key, for the rate limit.
    pins: Mutex<HashMap<String, VecDeque<Instant>>>,
}

impl Running {
    pub fn ip(&self) -> Ipv4Addr {
        self.shared.status.lock().unwrap_or_else(|p| p.into_inner()).0.ip
    }

    pub fn address(&self) -> &str {
        &self.address
    }

    pub fn pc(&self) -> &str {
        &self.pc
    }

    /// A fresh look at NetBird's peers (from the watcher).
    pub fn update_peers(&self, status: Status) {
        *self.shared.status.lock().unwrap_or_else(|p| p.into_inner()) = (status, Instant::now());
    }

    /// Stops serving; the port closes.
    pub fn stop(self) {
        self.server.unblock();
        trace!("phone: stopped serving {}", self.address);
    }
}

/// Starts serving on `status`'s NetBird address.
pub fn start(app: &AppHandle, status: Status) -> Result<Running, String> {
    let server = Server::http(SocketAddr::new(IpAddr::V4(status.ip), PORT)).map_err(|e| {
        if e.to_string().contains("10048") {
            format!("port {PORT} is in use")
        } else {
            e.to_string()
        }
    })?;
    let server = Arc::new(server);
    let address = format!("http://{}:{PORT}", status.fqdn);
    let pc = super::short(&status).to_string();
    let shared = Arc::new(Shared {
        status: Mutex::new((status, Instant::now())),
        in_flight: AtomicUsize::new(0),
        pins: Mutex::new(HashMap::new()),
    });
    let (app, listening, threads) = (app.clone(), server.clone(), shared.clone());
    std::thread::Builder::new()
        .name("tack-phone".into())
        .spawn(move || {
            for request in listening.incoming_requests() {
                if threads.in_flight.fetch_add(1, Ordering::SeqCst) >= MAX_IN_FLIGHT {
                    threads.in_flight.fetch_sub(1, Ordering::SeqCst);
                    let _ = request.respond(Response::from_string("Busy").with_status_code(503));
                    continue;
                }
                let (app, shared) = (app.clone(), threads.clone());
                std::thread::spawn(move || {
                    handle(&app, &shared, request);
                    shared.in_flight.fetch_sub(1, Ordering::SeqCst);
                });
            }
        })
        .map_err(|e| e.to_string())?;
    Ok(Running { server, shared, address, pc })
}

impl Shared {
    /// This PC's status and the peer at `ip`, if NetBird lists one there.
    fn peer(&self, ip: Ipv4Addr) -> Option<(Status, Peer)> {
        let mut guard = self.status.lock().unwrap_or_else(|p| p.into_inner());
        if guard.0.peer(ip).is_none() && guard.1.elapsed() >= RECHECK {
            guard.1 = Instant::now();
            if let Some(fresh) = netbird::status().filter(|s| s.ip == guard.0.ip) {
                guard.0 = fresh;
            }
        }
        let peer = guard.0.peer(ip)?.clone();
        Some((guard.0.clone(), peer))
    }

    /// Counts a pin from `key`; false when it is over the limit.
    fn may_pin(&self, key: &str) -> bool {
        let mut pins = self.pins.lock().unwrap_or_else(|p| p.into_inner());
        let recent = pins.entry(key.to_string()).or_default();
        let minute_ago = Instant::now() - Duration::from_secs(60);
        while recent.front().is_some_and(|t| *t < minute_ago) {
            recent.pop_front();
        }
        if recent.len() >= PINS_PER_MINUTE {
            return false;
        }
        recent.push_back(Instant::now());
        true
    }
}

fn handle(app: &AppHandle, shared: &Shared, request: Request) {
    let Some(IpAddr::V4(ip)) = request.remote_addr().map(|a| a.ip()) else {
        return deny(request, 403);
    };
    let Some((me, peer)) = shared.peer(ip) else {
        trace!("phone: refused {ip}, not a NetBird peer");
        return deny(request, 403);
    };
    let host = header(&request, "Host").unwrap_or_default().to_ascii_lowercase();
    if host != format!("{}:{PORT}", me.fqdn.to_ascii_lowercase()) && host != format!("{}:{PORT}", me.ip) {
        trace!("phone: refused {}, Host {host:?}", peer.short_name());
        return deny(request, 421);
    }
    // A browser marks where a request comes from; only this page itself may
    // call (absent for a typed address, and for scripts like curl).
    let site = header(&request, "Sec-Fetch-Site").unwrap_or_default().to_ascii_lowercase();
    let origin = header(&request, "Origin");
    if !matches!(site.as_str(), "" | "same-origin" | "none")
        || origin.is_some_and(|o| o.to_ascii_lowercase() != format!("http://{host}"))
    {
        trace!("phone: refused {}, from another site", peer.short_name());
        return deny(request, 403);
    }

    let (method, path) = route(&request);
    match (&method, path.as_str()) {
        (Method::Get, "/") => send(request, Response::from_string(PAGE), "text/html; charset=utf-8"),
        (Method::Get, "/phone.js") => send(request, Response::from_string(SCRIPT), "text/javascript; charset=utf-8"),
        (Method::Get, "/phone.css") => send(request, Response::from_string(STYLE), "text/css; charset=utf-8"),
        (Method::Get, "/icon.png") => send(request, Response::from_data(ICON), "image/png"),
        (Method::Get, "/manifest.webmanifest") => {
            send(request, Response::from_string(MANIFEST), "application/manifest+json")
        }
        (Method::Post, "/api/ask") => match super::ask_again(app, &peer) {
            Verdict::Allowed => send_json(request, 200, json!({ "state": "allowed" })),
            verdict => refused(request, verdict, &me, &peer),
        },
        (Method::Get, "/api/board") | (Method::Post, "/api/pin") => gated(app, shared, request, &me, &peer),
        (Method::Get, p) if p.starts_with("/api/print/") => gated(app, shared, request, &me, &peer),
        (Method::Get | Method::Post, _) => deny(request, 404),
        _ => deny(request, 405),
    }
}

/// An API route: served to an allowed device, the "ask" answer otherwise.
fn gated(app: &AppHandle, shared: &Shared, request: Request, me: &Status, peer: &Peer) {
    match super::admit(app, peer) {
        Verdict::Allowed => serve_api(app, shared, request, me, peer),
        verdict => refused(request, verdict, me, peer),
    }
}

/// The board, a print's picture, or a pin, for an allowed device.
fn serve_api(app: &AppHandle, shared: &Shared, mut request: Request, me: &Status, peer: &Peer) {
    let (method, path) = route(&request);
    match (&method, path.as_str()) {
        (Method::Get, "/api/board") => {
            let body = serde_json::to_string(&board(app, me, peer)).unwrap_or_default();
            send(request, Response::from_string(body), "application/json")
        }
        (Method::Get, p) if p.starts_with("/api/print/") => print_file(app, request, &p["/api/print/".len()..]),
        (Method::Post, "/api/pin") => {
            if !shared.may_pin(&peer.key) {
                return send_text(request, 429, "Slow down a little");
            }
            let kind = header(&request, "Content-Type").unwrap_or_default();
            let mut body = Vec::new();
            let read = request.as_reader().take(MAX_BODY as u64 + 1).read_to_end(&mut body);
            if read.is_err() || body.len() > MAX_BODY {
                return send_text(request, 413, "That's too large to pin");
            }
            let (code, said) = pin(app, peer, &kind, &body);
            send_text(request, code, said)
        }
        _ => deny(request, 404),
    }
}

/// Not (yet) allowed: what the page shows instead of the board.
fn refused(request: Request, verdict: Verdict, me: &Status, peer: &Peer) {
    let state = if matches!(verdict, Verdict::Waiting { .. }) { "waiting" } else { "denied" };
    send_json(request, 403, json!({ "state": state, "pc": super::short(me), "you": peer.short_name() }));
}

/// Pins what the phone sent: a JPEG or PNG, or text.
fn pin(app: &AppHandle, peer: &Peer, kind: &str, body: &[u8]) -> (u16, &'static str) {
    let kind = kind.split(';').next().unwrap_or("").trim().to_ascii_lowercase();
    match kind.as_str() {
        "image/jpeg" | "image/png" => {
            match notes::pin_image_bytes(app, &format!("photo from {}", peer.short_name()), body) {
                Ok(_line) => {
                    trace!("phone: {_line}");
                    (200, "Pinned")
                }
                Err(_line) => {
                    trace!("phone: {_line}");
                    (422, "Couldn't pin that picture")
                }
            }
        }
        "text/plain" => {
            let Ok(text) = std::str::from_utf8(body) else { return (415, "Text must be UTF-8") };
            let Some(body) = note::body(text) else { return (422, "Nothing to pin") };
            if lock(app).board.find_note(&body.text).is_some() {
                return (409, "Already on your board");
            }
            let _line = notes::pin_text(app, text);
            trace!("phone: text from {}: {_line}", peer.short_name());
            (200, "Pinned")
        }
        _ => (415, "Only photos and text can be pinned"),
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct BoardView {
    /// This PC's short name, for the page's title.
    pc: String,
    /// The phone's short name, as NetBird calls it.
    you: String,
    prints: Vec<PrintView>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PrintView {
    id: String,
    note: bool,
    thumb: String,
    width: u32,
    height: u32,
    text: Option<String>,
    link: Option<String>,
    pinned_at: u64,
    kept: bool,
}

fn board(app: &AppHandle, me: &Status, peer: &Peer) -> BoardView {
    let s = lock(app);
    let prints = s
        .board
        .prints()
        .iter()
        .map(|p| PrintView {
            id: p.id.clone(),
            note: p.kind == Kind::Note,
            thumb: p.thumb.clone(),
            width: p.width,
            height: p.height,
            text: p.note.as_ref().map(|n| n.text.clone()),
            link: p.note.as_ref().and_then(|n| n.link.clone()),
            pinned_at: p.pinned_at,
            kept: p.kept,
        })
        .collect();
    BoardView { pc: super::short(me).to_string(), you: peer.short_name().to_string(), prints }
}

/// A print's full picture.
fn print_file(app: &AppHandle, request: Request, id: &str) {
    let path = {
        let s = lock(app);
        s.board.find(id).filter(|p| p.kind == Kind::Image).map(|p| p.path.clone())
    };
    let Some(path) = path else { return deny(request, 404) };
    // A capture from a moment ago may still be on its way to disk.
    crate::captures::wait_saved(&path);
    let Ok(bytes) = std::fs::read(&path) else { return deny(request, 404) };
    let ext = path.extension().and_then(|e| e.to_str()).unwrap_or("").to_ascii_lowercase();
    let kind = if ext == "png" { "image/png" } else { "image/jpeg" };
    send(request, Response::from_data(bytes), kind);
}

/// The request's method and path, without the query.
fn route(request: &Request) -> (Method, String) {
    (request.method().clone(), request.url().split('?').next().unwrap_or("/").to_string())
}

fn header(request: &Request, name: &str) -> Option<String> {
    request
        .headers()
        .iter()
        .find(|h| h.field.as_str().as_str().eq_ignore_ascii_case(name))
        .map(|h| h.value.as_str().to_string())
}

fn send<R: Read>(request: Request, mut response: Response<R>, kind: &str) {
    for (field, value) in [
        ("Content-Type", kind),
        ("Content-Security-Policy", CSP),
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Referrer-Policy", "no-referrer"),
        ("Cross-Origin-Opener-Policy", "same-origin"),
        ("Cross-Origin-Resource-Policy", "same-origin"),
        ("Cache-Control", "no-store"),
    ] {
        if let Ok(h) = Header::from_bytes(field.as_bytes(), value.as_bytes()) {
            response.add_header(h);
        }
    }
    let _ = request.respond(response);
}

fn send_text(request: Request, code: u16, text: &str) {
    send(request, Response::from_string(text).with_status_code(code), "text/plain; charset=utf-8");
}

fn send_json(request: Request, code: u16, value: serde_json::Value) {
    send(request, Response::from_string(value.to_string()).with_status_code(code), "application/json");
}

fn deny(request: Request, code: u16) {
    let _ = request.respond(Response::empty(code));
}
