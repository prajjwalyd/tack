//! Where the pointer is and what is under it, read straight from Win32. The
//! board window is click-through most of the time, so it cannot rely on its
//! own mouse events.

use windows::Win32::Foundation::POINT;
use windows::Win32::UI::Input::KeyboardAndMouse::GetAsyncKeyState;
use windows::Win32::UI::WindowsAndMessaging::{
    GetAncestor, GetClassNameW, GetCursorInfo, GetCursorPos, GetGUIThreadInfo, LoadCursorW, WindowFromPoint,
    CURSORINFO, GA_ROOT, GUITHREADINFO, GUI_INMOVESIZE, IDC_ARROW, IDC_IBEAM, IDC_SIZEALL, IDC_SIZENESW, IDC_SIZENS,
    IDC_SIZENWSE, IDC_SIZEWE,
};

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

/// What the pointer looks like, as far as telling a drag goes.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum CursorKind {
    /// The plain arrow: moving a window, a scroll bar, a slider.
    Arrow,
    /// The text cursor: selecting text.
    Text,
    /// A resize arrow: sizing a window or a pane.
    Size,
    /// Anything else, such as the cursors Windows shows while something is
    /// dragged between apps (copy, move, link, no drop).
    Other,
}

impl CursorKind {
    /// Whether a drag of something between apps may be under way: then
    /// Windows shows its own drag cursors, never these.
    pub fn may_be_dragging(self) -> bool {
        self == CursorKind::Other
    }
}

/// The current pointer's kind. Standard cursors are shared, so comparing
/// handles with the system's own tells them apart.
pub fn cursor_kind() -> CursorKind {
    unsafe {
        let mut info = CURSORINFO { cbSize: std::mem::size_of::<CURSORINFO>() as u32, ..Default::default() };
        if GetCursorInfo(&mut info).is_err() || info.hCursor.is_invalid() {
            return CursorKind::Other;
        }
        let is = |name| LoadCursorW(None, name).is_ok_and(|c| c == info.hCursor);
        if is(IDC_ARROW) {
            CursorKind::Arrow
        } else if is(IDC_IBEAM) {
            CursorKind::Text
        } else if [IDC_SIZEALL, IDC_SIZENS, IDC_SIZEWE, IDC_SIZENESW, IDC_SIZENWSE].into_iter().any(is) {
            CursorKind::Size
        } else {
            CursorKind::Other
        }
    }
}

/// Whether the window in front is being moved or sized right now (dragged
/// by its title bar, perhaps to snap it to the top of the screen).
pub fn window_moving() -> bool {
    unsafe {
        let mut info = GUITHREADINFO { cbSize: std::mem::size_of::<GUITHREADINFO>() as u32, ..Default::default() };
        // Thread 0: the foreground thread.
        GetGUIThreadInfo(0, &mut info).is_ok() && info.flags.contains(GUI_INMOVESIZE)
    }
}
