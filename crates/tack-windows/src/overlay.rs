//! The board's window: a transparent, topmost overlay across the top of one
//! monitor, never activated and click-through except where the board is.
//!
//! Driven with plain Win32 calls rather than Tauri's setters: tao rewrites the
//! whole extended style (and re-shows, activating, the window) whenever one of
//! its own flags changes. Every function taking an `HWND` must run on the UI
//! thread; from another thread the cross-thread SendMessage calls can
//! deadlock against the board lock.

use windows::Win32::Foundation::{HWND, POINT, RECT};
use windows::Win32::Graphics::Gdi::{GetMonitorInfoW, MonitorFromPoint, MONITORINFO, MONITOR_DEFAULTTONEAREST};
use windows::Win32::UI::HiDpi::{GetDpiForMonitor, MDT_EFFECTIVE_DPI};
use windows::Win32::UI::Shell::{
    SHQueryUserNotificationState, QUNS_BUSY, QUNS_PRESENTATION_MODE, QUNS_RUNNING_D3D_FULL_SCREEN,
};
use windows::Win32::UI::WindowsAndMessaging::{
    GetWindowLongPtrW, SetWindowLongPtrW, SetWindowPos, ShowWindow, GWL_EXSTYLE, HWND_TOPMOST, SWP_NOACTIVATE,
    SWP_NOSIZE, SWP_SHOWWINDOW, SW_HIDE, SW_SHOWNOACTIVATE, WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW,
    WS_EX_TRANSPARENT,
};

/// A monitor, in physical pixels.
pub struct Monitor {
    /// Without the taskbar.
    pub work: RECT,
    pub monitor: RECT,
    /// DPI / 96.
    pub scale: f64,
}

pub fn monitor_at(pt: POINT) -> Monitor {
    unsafe {
        let hmon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        let mut info = MONITORINFO { cbSize: std::mem::size_of::<MONITORINFO>() as u32, ..Default::default() };
        let _ = GetMonitorInfoW(hmon, &mut info);
        let (mut dx, mut dy) = (96u32, 96u32);
        let scale = if GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, &mut dx, &mut dy).is_ok() && dx > 0 {
            dx as f64 / 96.0
        } else {
            1.0
        };
        Monitor { work: info.rcWork, monitor: info.rcMonitor, scale }
    }
}

/// True while Windows reports a full-screen Direct3D app, presentation mode
/// or a busy state; Tack then does not reveal on its own.
pub fn fullscreen_app_in_front() -> bool {
    match unsafe { SHQueryUserNotificationState() } {
        Ok(state) => state == QUNS_BUSY || state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE,
        Err(_) => false,
    }
}

/// The raw `QUERY_USER_NOTIFICATION_STATE` behind [`fullscreen_app_in_front`]
/// (2 busy, 3 Direct3D full screen, 4 presentation, 5 normal, ...), for logs.
pub fn notification_state() -> i32 {
    unsafe { SHQueryUserNotificationState() }.map_or(-1, |state| state.0)
}

/// Once at startup: the window never takes focus and never shows up in
/// Alt+Tab or the taskbar.
pub fn make_unfocusable(hwnd: HWND) {
    unsafe {
        let ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        let ex = ex | (WS_EX_NOACTIVATE.0 | WS_EX_TOOLWINDOW.0) as isize;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, ex);
    }
}

/// Turns mouse pass-through on or off. The window is as wide as the monitor
/// and the board much narrower, so clicks are let through to the apps below
/// except while the pointer is on the board.
pub fn set_click_through(hwnd: HWND, ignore: bool) {
    unsafe {
        let ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        let bits = (WS_EX_TRANSPARENT.0 | WS_EX_LAYERED.0) as isize;
        let new = if ignore { ex | bits } else { ex & !bits };
        if new != ex {
            SetWindowLongPtrW(hwnd, GWL_EXSTYLE, new);
        }
    }
}

/// Shows the window, click-through and without activating it, at `pos` with
/// `size` (physical px).
pub fn show_at(hwnd: HWND, pos: (i32, i32), size: (i32, i32)) {
    set_click_through(hwnd, true);
    unsafe {
        // Move first, then size: crossing to a monitor with another DPI
        // makes tao resize the window on WM_DPICHANGED, and the second
        // call puts the size right in the new monitor's pixels.
        let _ = SetWindowPos(hwnd, Some(HWND_TOPMOST), pos.0, pos.1, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        let _ = SetWindowPos(hwnd, Some(HWND_TOPMOST), pos.0, pos.1, size.0, size.1, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        let _ = ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }
}

pub fn hide(hwnd: HWND) {
    unsafe {
        let _ = ShowWindow(hwnd, SW_HIDE);
    }
}
