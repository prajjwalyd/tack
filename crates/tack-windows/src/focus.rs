//! Keyboard focus for the board, only when the keyboard asked for it. The
//! window is normally `WS_EX_NOACTIVATE`, so the edge, a new capture or a
//! click never pulls the keyboard away from the app in use. The show-or-hide
//! shortcut makes it activatable and brings it to the foreground ([`take`]);
//! when it goes, the previous window gets the keyboard back ([`give_back`]).
//! Every function here must run on the UI thread.

use windows::core::BOOL;
use windows::Win32::Foundation::{HWND, LPARAM};
use windows::Win32::System::Threading::{AttachThreadInput, GetCurrentThreadId};
use windows::Win32::UI::WindowsAndMessaging::{
    BringWindowToTop, EnumWindows, GetForegroundWindow, GetWindow, GetWindowLongPtrW, GetWindowThreadProcessId,
    IsWindow, IsWindowVisible, PeekMessageW, SetForegroundWindow, SetWindowLongPtrW, GWL_EXSTYLE, GW_OWNER, MSG,
    PM_NOREMOVE, WM_USER, WS_EX_NOACTIVATE,
};

/// Makes `hwnd` the foreground window. Returns the window that had the
/// foreground before (to give it back later), or `None` if `hwnd` could not
/// take it.
pub fn take(hwnd: HWND) -> Option<HWND> {
    unsafe {
        let previous = GetForegroundWindow();
        set_no_activate(hwnd, false);
        if previous == hwnd {
            return Some(previous);
        }
        force_foreground(hwnd);
        if GetForegroundWindow() == hwnd {
            Some(previous)
        } else {
            set_no_activate(hwnd, true);
            None
        }
    }
}

/// Gives the foreground back to `previous` if the board still has it (if the
/// user clicked another window meanwhile, that one keeps it), and makes the
/// board unfocusable again.
pub fn give_back(hwnd: HWND, previous: Option<HWND>) {
    unsafe {
        if GetForegroundWindow() == hwnd {
            if let Some(previous) = previous.filter(|p| *p != hwnd && !p.is_invalid()) {
                if IsWindow(Some(previous)).as_bool() && IsWindowVisible(previous).as_bool() {
                    force_foreground(previous);
                }
            }
        }
        set_no_activate(hwnd, true);
    }
}

/// Brings `hwnd` to the foreground. Windows only lets the app the user is
/// working with change the foreground; sharing that app's input state for a
/// moment counts as being it, with no key pressed on anyone's behalf.
unsafe fn force_foreground(hwnd: HWND) {
    if SetForegroundWindow(hwnd).as_bool() {
        return;
    }
    // AttachThreadInput needs this thread to have a message queue; asking
    // for a message makes one if it has none yet.
    let mut msg = MSG::default();
    let _ = PeekMessageW(&mut msg, None, WM_USER, WM_USER, PM_NOREMOVE);
    let ours = GetCurrentThreadId();
    let theirs = GetWindowThreadProcessId(GetForegroundWindow(), None);
    let attached = theirs != 0 && theirs != ours && AttachThreadInput(ours, theirs, true).as_bool();
    let _ = BringWindowToTop(hwnd);
    let _ = SetForegroundWindow(hwnd);
    if attached {
        let _ = AttachThreadInput(ours, theirs, false);
    }
}

/// The process whose window is in front.
pub fn foreground_process() -> Option<u32> {
    let mut pid = 0u32;
    unsafe { GetWindowThreadProcessId(GetForegroundWindow(), Some(&mut pid)) };
    (pid != 0).then_some(pid)
}

/// For tests: brings the main window of process `pid` to the front. Returns
/// the window that was in front before (as a raw handle, to give the
/// foreground back), or `None` if that process's window did not get it.
pub fn bring_to_front(pid: u32) -> Option<isize> {
    unsafe {
        let previous = GetForegroundWindow();
        let mut found = (pid, HWND::default());
        let _ = EnumWindows(Some(find_window), LPARAM(&mut found as *mut (u32, HWND) as isize));
        let target = found.1;
        if target.is_invalid() {
            return None;
        }
        force_foreground(target);
        (foreground_process() == Some(pid)).then_some(previous.0 as isize)
    }
}

/// Gives the foreground back to a window [`bring_to_front`] took it from.
pub fn return_foreground(previous: isize) {
    let hwnd = HWND(previous as *mut _);
    unsafe {
        if !hwnd.is_invalid() && IsWindow(Some(hwnd)).as_bool() {
            force_foreground(hwnd);
        }
    }
}

unsafe extern "system" fn find_window(hwnd: HWND, data: LPARAM) -> BOOL {
    let found = &mut *(data.0 as *mut (u32, HWND));
    let mut pid = 0u32;
    GetWindowThreadProcessId(hwnd, Some(&mut pid));
    let top_level = GetWindow(hwnd, GW_OWNER).map_or(true, |owner| owner.is_invalid());
    if pid == found.0 && IsWindowVisible(hwnd).as_bool() && top_level {
        found.1 = hwnd;
        return BOOL(0);
    }
    BOOL(1)
}

fn set_no_activate(hwnd: HWND, on: bool) {
    unsafe {
        let ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        let bit = WS_EX_NOACTIVATE.0 as isize;
        let new = if on { ex | bit } else { ex & !bit };
        if new != ex {
            SetWindowLongPtrW(hwnd, GWL_EXSTYLE, new);
        }
    }
}
