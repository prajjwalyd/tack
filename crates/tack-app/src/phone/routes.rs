//! What the phone board serves, and to whom. The page and its files go to
//! any NetBird peer: they hold nothing of the board. Everything under
//! `/api/` needs a device the user allowed (`super::admit`).
//!
//! Every request must name this PC in Host, so a web page cannot point a
//! name of its own at this address (DNS rebinding). A request carrying an
//! Origin must come from this page. API calls that act or may raise the
//! "Let pixel use your board?" question must also carry `X-Tack: 1`, which
//! another site open on the phone cannot add without a CORS preflight this
//! server never answers; without it, an API request is served only to a
//! device already allowed, and never asks. (Over plain HTTP browsers send no
//! Fetch Metadata, so that cannot be relied on.)

use std::collections::hash_map::DefaultHasher;
use std::hash::{Hash, Hasher};
use std::net::TcpStream;

use base64::Engine;
use serde::Serialize;
use serde_json::json;
use tack_core::netbird::{Peer, Status};
use tack_core::phone::Verdict;
use tack_core::{note, Kind, Print};
use tauri::AppHandle;

use super::http::{self, Head, Response};
use super::server::Shared;
use super::PORT;
use crate::notes;
use crate::state::lock;

/// The page may load only itself.
pub const CSP: &str = concat!(
    "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; ",
    "connect-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
);

/// The largest pin: a photo the page has already scaled down, or a
/// screenshot sent as it is.
const MAX_PIN: usize = 20 * 1024 * 1024;
/// The largest picture file served to the viewer.
const MAX_SERVED: u64 = 64 * 1024 * 1024;
/// Pictures are addressed by content (`?v=`), so they may be kept.
const KEEP: &str = "private, max-age=31536000, immutable";

const PAGE: &str = include_str!("../../../../ui/phone/index.html");
const SCRIPT: &str = include_str!("../../../../ui/phone/phone.js");
const STYLE: &str = include_str!("../../../../ui/phone/phone.css");
const ICON: &[u8] = include_bytes!("../../icons/128x128@2x.png");
/// Lets Android's "Add to Home screen" use Tack's name and icon.
const MANIFEST: &str = r##"{"name":"Tack","short_name":"Tack","start_url":"/","display":"standalone","background_color":"#153328","theme_color":"#1c4234","icons":[{"src":"/icon.png","sizes":"256x256","type":"image/png"}]}"##;

pub(super) fn handle(
    app: &AppHandle,
    shared: &Shared,
    stream: &mut TcpStream,
    head: &mut Head,
    me: &Status,
    peer: &Peer,
) -> Response {
    let host = head.header("Host").unwrap_or_default().to_ascii_lowercase();
    if host != format!("{}:{PORT}", me.fqdn.to_ascii_lowercase()) && host != format!("{}:{PORT}", me.ip) {
        trace!("phone: refused {}, Host {host:?}", peer.short_name());
        return Response::empty(421);
    }
    if head.header("Origin").is_some_and(|o| !o.eq_ignore_ascii_case(&format!("http://{host}"))) {
        trace!("phone: refused {}, from another site", peer.short_name());
        return Response::empty(403);
    }
    let ours = head.header("X-Tack") == Some("1");
    let path = head.path.clone();
    match (head.method.as_str(), path.as_str()) {
        ("GET", "/") => Response::bytes(200, "text/html; charset=utf-8", PAGE),
        ("GET", "/phone.js") => Response::bytes(200, "text/javascript; charset=utf-8", SCRIPT),
        ("GET", "/phone.css") => Response::bytes(200, "text/css; charset=utf-8", STYLE),
        ("GET", "/icon.png") => Response::bytes(200, "image/png", ICON),
        ("GET", "/manifest.webmanifest") => Response::bytes(200, "application/manifest+json", MANIFEST),
        ("POST", "/api/ask") if ours => match super::ask_again(app, peer) {
            Verdict::Allowed => json_response(200, json!({ "state": "allowed" })),
            verdict => refused(&verdict, me, peer),
        },
        ("GET", "/api/board") => match gate(app, shared, peer, ours) {
            Verdict::Allowed => board(app, head, me, peer),
            verdict => refused(&verdict, me, peer),
        },
        ("GET", p) if p.starts_with("/api/thumb/") => match gate(app, shared, peer, false) {
            Verdict::Allowed => thumb(app, &p["/api/thumb/".len()..]),
            _ => Response::empty(403),
        },
        ("GET", p) if p.starts_with("/api/print/") => match gate(app, shared, peer, false) {
            Verdict::Allowed => print_file(app, &p["/api/print/".len()..]),
            _ => Response::empty(403),
        },
        ("POST", "/api/pin") if ours => match gate(app, shared, peer, true) {
            Verdict::Allowed => pin(app, shared, stream, head, peer),
            verdict => refused(&verdict, me, peer),
        },
        ("POST", p) if p.starts_with("/api/") => Response::empty(403),
        ("GET" | "POST", _) => Response::empty(404),
        _ => Response::empty(405),
    }
}

/// Whether `peer` may use the board, from a fresh look at NetBird's peers.
/// Only a request from the page itself (`ours`) may ask the question.
fn gate(app: &AppHandle, shared: &Shared, peer: &Peer, ours: bool) -> Verdict {
    let Some((_, current)) = shared.peer(peer.ip, true) else { return Verdict::Refused };
    if current.key != peer.key {
        return Verdict::Refused;
    }
    if ours {
        super::admit(app, peer)
    } else if super::allowed(app, &peer.key) {
        Verdict::Allowed
    } else {
        Verdict::Refused
    }
}

/// Not (yet) allowed: what the page shows instead of the board.
fn refused(verdict: &Verdict, me: &Status, peer: &Peer) -> Response {
    let (pc, you) = (super::short(me), peer.short_name());
    let body = match verdict {
        Verdict::Waiting { code, .. } => json!({ "state": "waiting", "code": code, "pc": pc, "you": you }),
        Verdict::Busy => json!({ "state": "busy", "pc": pc, "you": you }),
        _ => json!({ "state": "denied", "pc": pc, "you": you }),
    };
    json_response(403, body)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct BoardView<'a> {
    pc: &'a str,
    you: &'a str,
    prints: Vec<PrintView>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PrintView {
    id: String,
    note: bool,
    /// The thumbnail's address ("" for a note), and the full picture's.
    thumb: String,
    full: String,
    width: u32,
    height: u32,
    text: Option<String>,
    link: Option<String>,
    pinned_at: u64,
    kept: bool,
}

/// The board, without its pictures (the page fetches those by address, and
/// keeps them). Answers 304 when nothing changed since the page last asked.
fn board(app: &AppHandle, head: &Head, me: &Status, peer: &Peer) -> Response {
    let prints: Vec<PrintView> = lock(app).board.prints().iter().map(view).collect();
    let body =
        serde_json::to_string(&BoardView { pc: super::short(me), you: peer.short_name(), prints }).unwrap_or_default();
    let tag = format!("\"{:016x}\"", digest(&body));
    if head.header("If-None-Match") == Some(tag.as_str()) {
        return Response::empty(304).header("ETag", tag);
    }
    Response::bytes(200, "application/json", body).header("ETag", tag)
}

fn view(p: &Print) -> PrintView {
    let image = p.kind == Kind::Image;
    let v = digest(&p.thumb);
    PrintView {
        id: p.id.clone(),
        note: !image,
        thumb: if image { format!("/api/thumb/{}?v={v:016x}", p.id) } else { String::new() },
        full: if image { format!("/api/print/{}?v={v:016x}", p.id) } else { String::new() },
        width: p.width,
        height: p.height,
        text: p.note.as_ref().map(|n| n.text.clone()),
        link: p.note.as_ref().and_then(|n| n.link.clone()),
        pinned_at: p.pinned_at,
        kept: p.kept,
    }
}

fn digest(s: &str) -> u64 {
    let mut h = DefaultHasher::new();
    s.hash(&mut h);
    h.finish()
}

/// A print's thumbnail, as the JPEG the board shows.
fn thumb(app: &AppHandle, id: &str) -> Response {
    let data = lock(app).board.find(id).filter(|p| p.kind == Kind::Image).map(|p| p.thumb.clone());
    let jpeg = data
        .as_deref()
        .and_then(|d| d.strip_prefix("data:image/jpeg;base64,"))
        .and_then(|b| base64::engine::general_purpose::STANDARD.decode(b).ok());
    match jpeg {
        Some(bytes) => Response::bytes(200, "image/jpeg", bytes).header("Cache-Control", KEEP),
        None => Response::empty(404),
    }
}

/// A print's full picture, streamed from its file.
fn print_file(app: &AppHandle, id: &str) -> Response {
    let path = lock(app).board.find(id).filter(|p| p.kind == Kind::Image).map(|p| p.path.clone());
    let Some(path) = path else { return Response::empty(404) };
    // A capture from a moment ago may still be on its way to disk.
    crate::captures::wait_saved(&path);
    let Ok(file) = std::fs::File::open(&path) else { return Response::empty(404) };
    let len = file.metadata().map(|m| m.len()).unwrap_or(0);
    if len == 0 || len > MAX_SERVED {
        return Response::empty(404);
    }
    let png = path.extension().and_then(|e| e.to_str()).is_some_and(|e| e.eq_ignore_ascii_case("png"));
    Response::file(if png { "image/png" } else { "image/jpeg" }, file, len).header("Cache-Control", KEEP)
}

/// Pins what the phone sent: a JPEG or PNG, or text.
fn pin(app: &AppHandle, shared: &Shared, stream: &mut TcpStream, head: &mut Head, peer: &Peer) -> Response {
    let kind = head.header("Content-Type").unwrap_or_default();
    let kind = kind.split(';').next().unwrap_or("").trim().to_ascii_lowercase();
    if !matches!(kind.as_str(), "image/jpeg" | "image/png" | "text/plain") {
        return text(415, "Only photos and text can be pinned");
    }
    if head.content_length > MAX_PIN {
        return text(413, "That's too large to pin");
    }
    if !shared.may_pin(&peer.key) {
        return text(429, "Slow down a little");
    }
    let Some(_turn) = shared.pin_turn() else { return text(503, "Busy, try again in a moment") };
    let Some(body) = http::read_body(stream, head, MAX_PIN) else { return text(413, "That didn't arrive in full") };
    if kind == "text/plain" {
        let Ok(text_body) = std::str::from_utf8(&body) else { return text(415, "Text must be UTF-8") };
        let Some(note_body) = note::body(text_body) else { return text(422, "Nothing to pin") };
        if lock(app).board.find_note(&note_body.text).is_some() {
            return text(409, "Already on your board");
        }
        let _line = notes::pin_text(app, text_body);
        trace!("phone: text from {}: {_line}", peer.short_name());
        return text(200, "Pinned");
    }
    match notes::pin_image_bytes(app, &format!("photo from {}", peer.short_name()), &body) {
        Ok(_line) => {
            trace!("phone: {_line}");
            text(200, "Pinned")
        }
        Err(_line) => {
            trace!("phone: {_line}");
            text(422, "Couldn't pin that picture")
        }
    }
}

fn text(status: u16, said: &str) -> Response {
    Response::bytes(status, "text/plain; charset=utf-8", said)
}

fn json_response(status: u16, value: serde_json::Value) -> Response {
    Response::bytes(status, "application/json", value.to_string())
}
