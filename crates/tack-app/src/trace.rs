//! Debug builds only: a timestamped stderr line for each step a reveal or a
//! tuck goes through (the request, the web view waking or going to sleep,
//! the window shown or hidden, each event sent), so a board that failed to
//! come down can be explained from the log alone. Use it through the
//! `trace!` macro in main.rs, which compiles to nothing in release builds.

use std::sync::OnceLock;
use std::time::Instant;

static START: OnceLock<Instant> = OnceLock::new();

/// Starts the clock the lines are stamped with.
pub fn start() {
    START.get_or_init(Instant::now);
}

/// Milliseconds since [`start`].
pub fn now_ms() -> f64 {
    START.get_or_init(Instant::now).elapsed().as_secs_f64() * 1000.0
}

pub fn line(args: std::fmt::Arguments) {
    let thread = std::thread::current();
    eprintln!("tack {:>10.1} [{}] {args}", now_ms(), thread.name().unwrap_or("?"));
}
