//! One board is plenty: a second copy would pin every screenshot twice. A
//! named mutex tells a second launch that Tack is already running.

use windows::core::HSTRING;
use windows::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS};
use windows::Win32::System::Threading::CreateMutexW;

/// True if another Tack holds the mutex. The first caller takes it and keeps
/// it for the life of the process.
///
/// Builds with debug assertions read `TACK_DEBUG_INSTANCE`: a test copy
/// started with it (and its own APPDATA, LOCALAPPDATA and TEMP) runs beside
/// the user's Tack instead of exiting.
pub fn already_running() -> bool {
    let mut name = String::from("Local\\app.tack.board");
    if cfg!(debug_assertions) {
        if let Ok(instance) = std::env::var("TACK_DEBUG_INSTANCE") {
            name.push('.');
            name.push_str(&instance);
        }
    }
    unsafe {
        // The handle is kept for the life of the process on purpose.
        match CreateMutexW(None, true, &HSTRING::from(name)) {
            Ok(_) => GetLastError() == ERROR_ALREADY_EXISTS,
            Err(_) => false,
        }
    }
}
