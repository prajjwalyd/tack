//! What the pointer poller needs to hear about without asking: the session
//! being locked, the display going off (nobody can reach the top edge then),
//! and the displays being rearranged. A hidden window on a thread of its own
//! gets the three as messages and sleeps in `GetMessageW` in between: 0
//! wake-ups per second.

use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};

use windows::core::{w, Result};
use windows::Win32::Foundation::{HANDLE, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Power::{RegisterPowerSettingNotification, POWERBROADCAST_SETTING};
use windows::Win32::System::RemoteDesktop::{WTSRegisterSessionNotification, NOTIFY_FOR_THIS_SESSION};
use windows::Win32::System::SystemServices::GUID_CONSOLE_DISPLAY_STATE;
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DispatchMessageW, GetMessageW, RegisterClassW, DEVICE_NOTIFY_WINDOW_HANDLE, MSG,
    PBT_POWERSETTINGCHANGE, WM_DISPLAYCHANGE, WM_POWERBROADCAST, WM_WTSSESSION_CHANGE, WNDCLASSW, WS_EX_TOOLWINDOW,
    WS_POPUP, WTS_CONSOLE_CONNECT, WTS_REMOTE_CONNECT, WTS_SESSION_LOCK, WTS_SESSION_LOGON, WTS_SESSION_UNLOCK,
};

use crate::edge_reveal;

static LOCKED: AtomicBool = AtomicBool::new(false);
static DISPLAY_OFF: AtomicBool = AtomicBool::new(false);
static DISPLAY_EPOCH: AtomicU32 = AtomicU32::new(0);

/// The session is locked or the display is off, as far as it has been told:
/// until it says otherwise, nothing needs watching.
pub(crate) fn asleep() -> bool {
    LOCKED.load(Ordering::Relaxed) || DISPLAY_OFF.load(Ordering::Relaxed)
}

/// Changes whenever the displays are rearranged, resized or swapped.
pub(crate) fn display_epoch() -> u32 {
    DISPLAY_EPOCH.load(Ordering::Relaxed)
}

/// Starts listening. What fails to register stays unheard: the flags then
/// stay awake, and the poller does what it always did.
pub(crate) fn start() {
    let spawned = std::thread::Builder::new().name("tack-session".into()).spawn(|| {
        if let Err(e) = unsafe { listen() } {
            eprintln!("tack: cannot listen for session changes: {e}");
        }
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot start the session thread: {e}");
    }
}

unsafe fn listen() -> Result<()> {
    let instance = GetModuleHandleW(None)?.into();
    let class = WNDCLASSW {
        lpfnWndProc: Some(window_proc),
        hInstance: instance,
        lpszClassName: w!("TackSession"),
        ..Default::default()
    };
    if RegisterClassW(&class) == 0 {
        return Err(windows::core::Error::from_thread());
    }
    // Never shown. Not message-only, which would miss the display change
    // broadcast.
    let hwnd = CreateWindowExW(
        WS_EX_TOOLWINDOW,
        class.lpszClassName,
        w!(""),
        WS_POPUP,
        0,
        0,
        0,
        0,
        None,
        None,
        Some(instance),
        None,
    )?;
    // Both last as long as the window does. The display state is sent once
    // at registration, so a start with the display off is known.
    if let Err(e) = WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_THIS_SESSION) {
        eprintln!("tack: cannot listen for lock changes: {e}");
    }
    if let Err(e) =
        RegisterPowerSettingNotification(HANDLE(hwnd.0), &GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE)
    {
        eprintln!("tack: cannot listen for display power changes: {e}");
    }
    let mut msg = MSG::default();
    while GetMessageW(&mut msg, None, 0, 0).0 > 0 {
        DispatchMessageW(&msg);
    }
    Ok(())
}

unsafe extern "system" fn window_proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_WTSSESSION_CHANGE => match wparam.0 as u32 {
            WTS_SESSION_LOCK => set(&LOCKED, true),
            // Coming back to a session that was away does not always unlock.
            WTS_SESSION_UNLOCK | WTS_SESSION_LOGON | WTS_CONSOLE_CONNECT | WTS_REMOTE_CONNECT => set(&LOCKED, false),
            _ => {}
        },
        WM_POWERBROADCAST if wparam.0 as u32 == PBT_POWERSETTINGCHANGE => {
            let setting = &*(lparam.0 as *const POWERBROADCAST_SETTING);
            if setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE && setting.DataLength >= 4 {
                // 0 off, 1 on, 2 dimmed.
                let state = std::ptr::read_unaligned(setting.Data.as_ptr().cast::<u32>());
                set(&DISPLAY_OFF, state == 0);
            }
        }
        WM_DISPLAYCHANGE => {
            DISPLAY_EPOCH.fetch_add(1, Ordering::Relaxed);
        }
        _ => {}
    }
    DefWindowProcW(hwnd, msg, wparam, lparam)
}

/// Records a change; the poller is woken when it ends one.
fn set(flag: &AtomicBool, on: bool) {
    flag.store(on, Ordering::Relaxed);
    if !on {
        edge_reveal::wake();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use windows::core::GUID;
    use windows::Win32::UI::WindowsAndMessaging::{WTS_SESSION_LOCK, WTS_SESSION_UNLOCK};

    fn send(msg: u32, wparam: usize, lparam: isize) {
        unsafe { window_proc(HWND::default(), msg, WPARAM(wparam), LPARAM(lparam)) };
    }

    /// The layout of POWERBROADCAST_SETTING with a DWORD of data.
    #[repr(C)]
    struct DisplayState {
        setting: GUID,
        len: u32,
        state: u32,
    }

    fn display(state: u32) {
        let message = DisplayState { setting: GUID_CONSOLE_DISPLAY_STATE, len: 4, state };
        send(WM_POWERBROADCAST, PBT_POWERSETTINGCHANGE as usize, &message as *const _ as isize);
    }

    #[test]
    fn it_sleeps_while_locked_or_dark_and_wakes_when_neither() {
        assert!(!asleep());
        send(WM_WTSSESSION_CHANGE, WTS_SESSION_LOCK as usize, 0);
        assert!(asleep());
        // Dark as well: unlocking is not enough.
        display(0);
        send(WM_WTSSESSION_CHANGE, WTS_SESSION_UNLOCK as usize, 0);
        assert!(asleep());
        // Dimmed is lit.
        display(2);
        assert!(!asleep());
        display(0);
        display(1);
        assert!(!asleep());
    }

    #[test]
    fn rearranged_displays_change_the_epoch() {
        let before = display_epoch();
        send(WM_DISPLAYCHANGE, 0, 0);
        assert_ne!(display_epoch(), before);
    }
}
