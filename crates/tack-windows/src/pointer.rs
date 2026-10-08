//! Where the pointer is and what is under it, read straight from Win32. The
//! board window is click-through most of the time, so it cannot rely on its
//! own mouse events.

use windows::Win32::Foundation::POINT;
use windows::Win32::UI::Input::KeyboardAndMouse::GetAsyncKeyState;
use windows::Win32::UI::WindowsAndMessaging::{GetAncestor, GetClassNameW, GetCursorPos, WindowFromPoint, GA_ROOT};

pub use windows::Win32::UI::Input::KeyboardAndMouse::{VK_LBUTTON, VK_RBUTTON};

pub fn cursor_pos() -> POINT {
    let mut pt = POINT::default();
    unsafe {
        let _ = GetCursorPos(&mut pt);
    }
    pt
}

/// The key or mouse button is held right now.
pub fn key_down(vk: u16) -> bool {
    unsafe { (GetAsyncKeyState(vk as i32) as u16 & 0x8000) != 0 }
}

/// The taskbar (any monitor's) or the notification area overflow is under
/// the point.
pub fn on_taskbar(pt: POINT) -> bool {
    const CLASSES: [&str; 4] =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"];
    unsafe {
        let hwnd = WindowFromPoint(pt);
        if hwnd.is_invalid() {
            return false;
        }
        let mut buf = [0u16; 64];
        let len = GetClassNameW(GetAncestor(hwnd, GA_ROOT), &mut buf).max(0) as usize;
        CLASSES.contains(&String::from_utf16_lossy(&buf[..len]).as_str())
    }
}
