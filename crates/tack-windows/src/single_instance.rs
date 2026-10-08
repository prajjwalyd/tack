//! One board is plenty: a second copy would pin every screenshot twice. A
//! named mutex tells a second launch that Tack is already running.

use windows::core::w;
use windows::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS};
use windows::Win32::System::Threading::CreateMutexW;

/// True if another Tack holds the mutex. The first caller takes it and keeps
/// it for the life of the process.
pub fn already_running() -> bool {
    unsafe {
        // The handle is kept for the life of the process on purpose.
        match CreateMutexW(None, true, w!("Local\\app.tack.board")) {
            Ok(_) => GetLastError() == ERROR_ALREADY_EXISTS,
            Err(_) => false,
        }
    }
}
