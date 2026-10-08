//! Ctrl+Alt+T toggles the board from anywhere.

use windows::Win32::UI::Input::KeyboardAndMouse::{RegisterHotKey, MOD_ALT, MOD_CONTROL, MOD_NOREPEAT};
use windows::Win32::UI::WindowsAndMessaging::{GetMessageW, MSG, WM_HOTKEY};

const HOTKEY_ID: i32 = 1;
const VK_T: u32 = 0x54;

/// Registers Ctrl+Alt+T and calls `on_press` each time it is pressed.
///
/// A thread of its own with a message loop: the hotkey is bound to the
/// thread that registers it, and this keeps it off the UI thread.
pub fn start(on_press: impl Fn() + Send + 'static) {
    std::thread::Builder::new()
        .name("tack-hotkey".into())
        .spawn(move || unsafe {
            if let Err(e) = RegisterHotKey(None, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_T) {
                // Another app owns the shortcut; the tray still works.
                eprintln!("tack: cannot register Ctrl+Alt+T: {e}");
                return;
            }
            let mut msg = MSG::default();
            while GetMessageW(&mut msg, None, 0, 0).0 > 0 {
                if msg.message == WM_HOTKEY && msg.wParam.0 == HOTKEY_ID as usize {
                    on_press();
                }
            }
        })
        .expect("hotkey thread");
}
