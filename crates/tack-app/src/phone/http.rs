//! Just enough HTTP/1.1 for the phone board, with hard limits: a request
//! head of at most [`MAX_HEAD`] bytes and [`MAX_HEADERS`] headers, a body
//! only when the route wants one and never past its cap, timeouts on every
//! read and write, and one request per connection. Parsing is `httparse`'s.

use std::io::{self, Read, Write};
use std::net::TcpStream;
use std::time::{Duration, Instant};

pub const MAX_HEAD: usize = 16 * 1024;
const MAX_HEADERS: usize = 32;
/// How long a client may take to send its request head.
pub const HEAD_TIMEOUT: Duration = Duration::from_secs(10);
/// How long a body may stall between reads, and take in all (a photo over
/// a slow relayed link).
const BODY_IDLE: Duration = Duration::from_secs(20);
const BODY_TOTAL: Duration = Duration::from_secs(120);
pub const WRITE_TIMEOUT: Duration = Duration::from_secs(30);

/// A request's head, and whatever of its body arrived with it.
pub struct Head {
    pub method: String,
    pub path: String,
    headers: Vec<(String, String)>,
    pub content_length: usize,
    early_body: Vec<u8>,
}

/// Why a request could not be read. Each maps to a status, if any is sent.
#[derive(Debug)]
pub enum Error {
    /// The client went away or stalled: nothing to answer.
    Gone,
    HeadTooLarge,
    Malformed,
    /// Chunked bodies are not needed by the page, so not supported.
    LengthRequired,
}

impl Error {
    pub fn status(&self) -> Option<u16> {
        match self {
            Error::Gone => None,
            Error::HeadTooLarge => Some(431),
            Error::Malformed => Some(400),
            Error::LengthRequired => Some(411),
        }
    }
}

impl Head {
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers.iter().find(|(k, _)| k.eq_ignore_ascii_case(name)).map(|(_, v)| v.as_str())
    }
}

/// Reads a request head (not its body).
pub fn read_head(stream: &mut TcpStream) -> Result<Head, Error> {
    let _ = stream.set_read_timeout(Some(HEAD_TIMEOUT));
    let deadline = Instant::now() + HEAD_TIMEOUT;
    let mut buf = Vec::with_capacity(1024);
    let mut chunk = [0u8; 2048];
    loop {
        if Instant::now() > deadline {
            return Err(Error::Gone);
        }
        let n = stream.read(&mut chunk).map_err(|_| Error::Gone)?;
        if n == 0 {
            return Err(Error::Gone);
        }
        buf.extend_from_slice(&chunk[..n]);
        let mut headers = [httparse::EMPTY_HEADER; MAX_HEADERS];
        let mut req = httparse::Request::new(&mut headers);
        match req.parse(&buf) {
            Ok(httparse::Status::Complete(len)) => return head_of(&req, buf[len..].to_vec()),
            Ok(httparse::Status::Partial) if buf.len() < MAX_HEAD => continue,
            Ok(httparse::Status::Partial) | Err(httparse::Error::TooManyHeaders) => return Err(Error::HeadTooLarge),
            Err(_) => return Err(Error::Malformed),
        }
    }
}

fn head_of(req: &httparse::Request, early_body: Vec<u8>) -> Result<Head, Error> {
    let method = req.method.ok_or(Error::Malformed)?.to_string();
    let target = req.path.ok_or(Error::Malformed)?;
    let path = target.split_once('?').map_or(target, |(path, _)| path);
    let mut headers = Vec::with_capacity(req.headers.len());
    for h in req.headers.iter() {
        let value = std::str::from_utf8(h.value).map_err(|_| Error::Malformed)?;
        headers.push((h.name.to_string(), value.trim().to_string()));
    }
    let mut head = Head { method, path: path.to_string(), headers, content_length: 0, early_body };
    if head.header("Transfer-Encoding").is_some() {
        return Err(Error::LengthRequired);
    }
    if let Some(length) = head.header("Content-Length") {
        head.content_length = length.parse().map_err(|_| Error::Malformed)?;
    }
    Ok(head)
}

/// Reads the body, at most `max` bytes; None when it is larger or does not
/// arrive in time.
pub fn read_body(stream: &mut TcpStream, head: &mut Head, max: usize) -> Option<Vec<u8>> {
    if head.content_length > max {
        return None;
    }
    if head.header("Expect").is_some_and(|e| e.eq_ignore_ascii_case("100-continue")) {
        stream.write_all(b"HTTP/1.1 100 Continue\r\n\r\n").ok()?;
    }
    let mut body = std::mem::take(&mut head.early_body);
    body.truncate(head.content_length);
    body.reserve_exact(head.content_length - body.len());
    let _ = stream.set_read_timeout(Some(BODY_IDLE));
    let deadline = Instant::now() + BODY_TOTAL;
    let mut chunk = vec![0u8; 64 * 1024];
    while body.len() < head.content_length {
        if Instant::now() > deadline {
            return None;
        }
        let want = (head.content_length - body.len()).min(chunk.len());
        let n = stream.read(&mut chunk[..want]).ok()?;
        if n == 0 {
            return None;
        }
        body.extend_from_slice(&chunk[..n]);
    }
    Some(body)
}

/// A response: status, type, body, and any extra headers.
pub struct Response {
    status: u16,
    kind: &'static str,
    body: Body,
    headers: Vec<(&'static str, String)>,
}

enum Body {
    Bytes(Vec<u8>),
    File(std::fs::File, u64),
}

impl Response {
    pub fn bytes(status: u16, kind: &'static str, body: impl Into<Vec<u8>>) -> Response {
        Response { status, kind, body: Body::Bytes(body.into()), headers: Vec::new() }
    }

    pub fn empty(status: u16) -> Response {
        Response::bytes(status, "text/plain; charset=utf-8", Vec::new())
    }

    pub fn file(kind: &'static str, file: std::fs::File, len: u64) -> Response {
        Response { status: 200, kind, body: Body::File(file, len), headers: Vec::new() }
    }

    pub fn header(mut self, name: &'static str, value: impl Into<String>) -> Response {
        self.headers.push((name, value.into()));
        self
    }

    /// Writes it, with the headers every response carries, and closes.
    pub fn send(self, stream: &mut TcpStream, csp: &str) -> io::Result<()> {
        let _ = stream.set_write_timeout(Some(WRITE_TIMEOUT));
        let len = match &self.body {
            Body::Bytes(b) => b.len() as u64,
            Body::File(_, len) => *len,
        };
        let mut head = format!(
            "HTTP/1.1 {} {}\r\nContent-Type: {}\r\nContent-Length: {len}\r\nConnection: close\r\n\
             Content-Security-Policy: {csp}\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\n\
             Referrer-Policy: no-referrer\r\nCross-Origin-Opener-Policy: same-origin\r\n\
             Cross-Origin-Resource-Policy: same-origin\r\n",
            self.status,
            reason(self.status),
            self.kind,
        );
        if !self.headers.iter().any(|(k, _)| k.eq_ignore_ascii_case("Cache-Control")) {
            head.push_str("Cache-Control: no-store\r\n");
        }
        for (name, value) in &self.headers {
            head.push_str(&format!("{name}: {value}\r\n"));
        }
        head.push_str("\r\n");
        stream.write_all(head.as_bytes())?;
        match self.body {
            Body::Bytes(b) => stream.write_all(&b)?,
            Body::File(file, len) => {
                io::copy(&mut file.take(len), stream)?;
            }
        }
        stream.flush()
    }
}

fn reason(status: u16) -> &'static str {
    match status {
        200 => "OK",
        202 => "Accepted",
        304 => "Not Modified",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        411 => "Length Required",
        413 => "Payload Too Large",
        415 => "Unsupported Media Type",
        421 => "Misdirected Request",
        422 => "Unprocessable Content",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        _ => "",
    }
}

#[cfg(test)]
mod tests {
    use std::net::{TcpListener, TcpStream};

    use super::*;

    /// A connected pair: what the client wrote, as the server reads it.
    fn sent(request: &[u8]) -> TcpStream {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let mut client = TcpStream::connect(listener.local_addr().unwrap()).unwrap();
        client.write_all(request).unwrap();
        // Keep the client open for the test's duration by leaking it: the
        // server side sees no EOF until the data is read.
        std::mem::forget(client);
        listener.accept().unwrap().0
    }

    #[test]
    fn reads_a_head_and_its_body() {
        let mut s = sent(b"POST /api/pin?v=1 HTTP/1.1\r\nHost: pc:7717\r\nContent-Length: 5\r\nX-Tack: 1\r\n\r\nhello");
        let mut head = read_head(&mut s).unwrap();
        assert_eq!((head.method.as_str(), head.path.as_str()), ("POST", "/api/pin"));
        assert_eq!(head.header("x-tack"), Some("1"));
        assert_eq!(read_body(&mut s, &mut head, 10).as_deref(), Some(&b"hello"[..]));
    }

    #[test]
    fn a_body_over_the_cap_is_never_read() {
        let mut s = sent(b"POST / HTTP/1.1\r\nContent-Length: 1000000000000\r\n\r\n");
        let mut head = read_head(&mut s).unwrap();
        assert!(read_body(&mut s, &mut head, 1024).is_none());
    }

    #[test]
    fn an_endless_head_is_refused() {
        let mut long = b"GET / HTTP/1.1\r\nX-Long: ".to_vec();
        long.extend(std::iter::repeat_n(b'a', MAX_HEAD + 10));
        let mut s = sent(&long);
        assert!(matches!(read_head(&mut s), Err(Error::HeadTooLarge)));
    }

    #[test]
    fn too_many_headers_are_refused() {
        let mut many = b"GET / HTTP/1.1\r\n".to_vec();
        for n in 0..40 {
            many.extend(format!("X-{n}: 1\r\n").as_bytes());
        }
        many.extend(b"\r\n");
        let mut s = sent(&many);
        assert!(matches!(read_head(&mut s), Err(Error::HeadTooLarge)));
    }

    #[test]
    fn chunked_bodies_are_not_taken() {
        let mut s = sent(b"POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n");
        assert!(matches!(read_head(&mut s), Err(Error::LengthRequired)));
    }

    #[test]
    fn a_bad_length_is_malformed() {
        let mut s = sent(b"POST / HTTP/1.1\r\nContent-Length: -1\r\n\r\n");
        assert!(matches!(read_head(&mut s), Err(Error::Malformed)));
    }
}
