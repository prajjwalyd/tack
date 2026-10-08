//! Pinning the selection (Win+Alt+C): Tack asks the app in front to copy
//! what is selected, reads the copy, and puts the user's clipboard back as
//! it was.
//!
//! 1. The shortcut's keys are still down when it fires. While Alt or Win is
//!    held, a no-op key goes in first ([`mask_menu`]), so letting go of Alt
//!    does not open the app's menu bar (and Win does not open Start).
//! 2. Tack waits until every modifier is released ([`ReleaseWait`]), so the
//!    Ctrl+C it sends is not read as Win+Alt+Ctrl+C.
//! 3. It notes the clipboard's sequence number, keeps a [`Snapshot`] of what
//!    is there, sends Ctrl+C with `SendInput` and waits up to
//!    [`COPY_PATIENCE`] for the number to change. No change: nothing was
//!    selected (or the app does not copy on Ctrl+C).
//! 4. It reads the copy ([`clipboard::read`]): text, a picture or files.
//!    Content marked private is never returned.
//! 5. It restores the snapshot, so the user's clipboard is as it was.
//!
//! Only this module, on the user's shortcut, ever reads text from the
//! clipboard; the snipping listener ignores everything while it runs.

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};

use image::DynamicImage;
use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{HINSTANCE, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Input::KeyboardAndMouse::{
    SendInput, INPUT, INPUT_0, INPUT_KEYBOARD, KEYBDINPUT, KEYBD_EVENT_FLAGS, KEYEVENTF_KEYUP, VIRTUAL_KEY, VK_C,
    VK_CONTROL, VK_LWIN, VK_MENU, VK_RWIN, VK_SHIFT,
};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, RegisterClassW, HWND_MESSAGE, WINDOW_EX_STYLE, WINDOW_STYLE,
    WNDCLASSW,
};

use crate::clipboard::{self, Content, Snapshot};
use crate::pointer;

/// How long the app in front gets to copy after Ctrl+C.
pub const COPY_PATIENCE: Duration = Duration::from_millis(400);
/// How long to wait for the shortcut's keys to be let go.
pub const RELEASE_PATIENCE: Duration = Duration::from_millis(1500);
/// The keys must stay up this long before Ctrl+C goes out.
const RELEASE_SETTLE: Duration = Duration::from_millis(30);
/// A key code Windows leaves unassigned: pressing it does nothing anywhere,
/// but it counts as "another key" while Alt or Win is down.
const MASK_KEY: u16 = 0xE8;
const POLL: Duration = Duration::from_millis(10);

/// True while a grab runs, so the snipping listener ignores the clipboard
/// changes it causes.
static GRABBING: AtomicBool = AtomicBool::new(false);

/// Whether a grab is changing the clipboard right now.
pub fn grabbing() -> bool {
    GRABBING.load(Ordering::SeqCst)
}

/// What the selection turned out to be.
pub enum Grabbed {
    Text(String),
    Image(DynamicImage),
    Files(Vec<PathBuf>),
    /// Nothing was copied: no selection, or an app that does not copy on
    /// Ctrl+C (or one running as administrator, which Tack cannot send keys to).
    Nothing,
    /// Copied, but marked private by the app (a password manager).
    Private,
    /// The shortcut's keys were held down too long; nothing was sent.
    KeysHeld,
}

/// A grab, and what became of the user's clipboard.
pub struct Grab {
    pub what: Grabbed,
    /// `None`: the clipboard never changed, so there was nothing to restore.
    pub restored: Option<Result<(), String>>,
}

/// Step 1, the moment the shortcut fires (on the hotkey thread): while Alt
/// or Win is held, a press of an unassigned key keeps their release from
/// opening the menu bar of the app in front, or Start.
pub fn mask_menu() {
    let held = [VK_MENU, VK_LWIN, VK_RWIN].iter().any(|vk| pointer::key_down(vk.0));
    if held {
        send(&[key(MASK_KEY, false), key(MASK_KEY, true)]);
    }
}

/// Steps 2 to 5. Blocks for up to about two seconds; call it on a thread of
/// its own, never the UI thread. With `only`, Ctrl+C is sent only if the
/// window in front belongs to that process (for tests: never anyone else's).
pub fn grab(only: Option<u32>) -> Grab {
    let mut wait = ReleaseWait::new(Instant::now());
    loop {
        match wait.step(modifiers_down(), Instant::now()) {
            Release::Ready => break,
            Release::Wait => std::thread::sleep(POLL),
            Release::TimedOut => return Grab { what: Grabbed::KeysHeld, restored: None },
        }
    }
    let Some(owner) = Owner::new() else {
        return Grab { what: Grabbed::Nothing, restored: None };
    };
    GRABBING.store(true, Ordering::SeqCst);
    let grab = copy_and_read(owner.0, only);
    // A last clipboard event may still be on its way to the listener.
    std::thread::spawn(|| {
        std::thread::sleep(Duration::from_millis(250));
        GRABBING.store(false, Ordering::SeqCst);
    });
    grab
}

fn copy_and_read(owner: HWND, only: Option<u32>) -> Grab {
    let before = clipboard::snapshot(owner);
    let sequence = clipboard::sequence();
    if only.is_some_and(|pid| crate::focus::foreground_process() != Some(pid)) {
        log("the expected window is not in front; no keys sent");
        return Grab { what: Grabbed::Nothing, restored: None };
    }
    let sent = send(&[key(VK_CONTROL.0, false), key(VK_C.0, false), key(VK_C.0, true), key(VK_CONTROL.0, true)]);
    if !sent {
        log("SendInput refused the keys");
        return Grab { what: Grabbed::Nothing, restored: None };
    }
    let give_up = Instant::now() + COPY_PATIENCE;
    while clipboard::sequence() == sequence {
        if Instant::now() >= give_up {
            log("the clipboard did not change after Ctrl+C");
            return Grab { what: Grabbed::Nothing, restored: None };
        }
        std::thread::sleep(POLL);
    }
    // Some apps set the clipboard in two steps; let the second land.
    std::thread::sleep(Duration::from_millis(40));
    let what = match clipboard::read(owner) {
        Content::Text(text) => Grabbed::Text(text),
        Content::Image(img) => Grabbed::Image(img),
        Content::Files(files) => Grabbed::Files(files),
        Content::Private => Grabbed::Private,
        Content::Empty => Grabbed::Nothing,
    };
    let restored = Some(match &before {
        Some(snapshot) => restore(owner, snapshot),
        None => Err("the clipboard could not be read before the copy".into()),
    });
    Grab { what, restored }
}

/// Debug builds say why a grab found nothing.
fn log(why: &str) {
    if cfg!(debug_assertions) {
        eprintln!("tack: pin selection: {why}");
    }
}

fn restore(owner: HWND, snapshot: &Snapshot) -> Result<(), String> {
    clipboard::restore(owner, snapshot)
}

/// Any modifier key is down.
fn modifiers_down() -> bool {
    [VK_MENU, VK_LWIN, VK_RWIN, VK_CONTROL, VK_SHIFT].iter().any(|vk| pointer::key_down(vk.0))
}

fn key(vk: u16, up: bool) -> INPUT {
    INPUT {
        r#type: INPUT_KEYBOARD,
        Anonymous: INPUT_0 {
            ki: KEYBDINPUT {
                wVk: VIRTUAL_KEY(vk),
                wScan: 0,
                dwFlags: if up { KEYEVENTF_KEYUP } else { KEYBD_EVENT_FLAGS(0) },
                time: 0,
                dwExtraInfo: 0,
            },
        },
    }
}

/// Sends the key events; false if Windows refused some (another desktop,
/// or an app with higher rights in front).
fn send(inputs: &[INPUT]) -> bool {
    let sent = unsafe { SendInput(inputs, std::mem::size_of::<INPUT>() as i32) };
    sent as usize == inputs.len()
}

/// A hidden message-only window to own the clipboard while the user's
/// contents are put back: `SetClipboardData` needs an owner.
struct Owner(HWND);

impl Owner {
    fn new() -> Option<Owner> {
        let class = w!("TackSelection");
        unsafe {
            let instance: HINSTANCE = GetModuleHandleW(PCWSTR::null()).ok()?.into();
            let wc = WNDCLASSW {
                lpfnWndProc: Some(wndproc),
                hInstance: instance,
                lpszClassName: class,
                ..Default::default()
            };
            // Fails harmlessly once the class exists.
            RegisterClassW(&wc);
            let hwnd = CreateWindowExW(
                WINDOW_EX_STYLE::default(),
                class,
                w!("Tack selection"),
                WINDOW_STYLE::default(),
                0,
                0,
                0,
                0,
                Some(HWND_MESSAGE),
                None,
                Some(instance),
                None,
            )
            .ok()?;
            Some(Owner(hwnd))
        }
    }
}

unsafe extern "system" fn wndproc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    DefWindowProcW(hwnd, msg, wparam, lparam)
}

impl Drop for Owner {
    fn drop(&mut self) {
        unsafe {
            let _ = DestroyWindow(self.0);
        }
    }
}

/// Where waiting for the shortcut's keys to be let go stands.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Release {
    /// Every modifier has been up for a moment: send Ctrl+C.
    Ready,
    /// Look again shortly.
    Wait,
    /// Still held after [`RELEASE_PATIENCE`]: give up, sending nothing.
    TimedOut,
}

/// Waits for the modifiers to be released and to stay released for
/// [`RELEASE_SETTLE`], giving up after [`RELEASE_PATIENCE`]. Fed one
/// reading at a time, so it can be tested without a keyboard.
pub struct ReleaseWait {
    started: Instant,
    up_since: Option<Instant>,
}

impl ReleaseWait {
    pub fn new(now: Instant) -> ReleaseWait {
        ReleaseWait { started: now, up_since: None }
    }

    /// `held`: any modifier is down at `now`.
    pub fn step(&mut self, held: bool, now: Instant) -> Release {
        if held {
            self.up_since = None;
            return if now.saturating_duration_since(self.started) >= RELEASE_PATIENCE {
                Release::TimedOut
            } else {
                Release::Wait
            };
        }
        let since = *self.up_since.get_or_insert(now);
        if now.saturating_duration_since(since) >= RELEASE_SETTLE {
            Release::Ready
        } else {
            Release::Wait
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ms(n: u64) -> Duration {
        Duration::from_millis(n)
    }

    #[test]
    fn keys_already_up_are_ready_after_a_short_settle() {
        let t0 = Instant::now();
        let mut wait = ReleaseWait::new(t0);
        assert_eq!(wait.step(false, t0), Release::Wait);
        assert_eq!(wait.step(false, t0 + ms(10)), Release::Wait);
        assert_eq!(wait.step(false, t0 + RELEASE_SETTLE), Release::Ready);
    }

    #[test]
    fn a_key_pressed_again_restarts_the_settle() {
        let t0 = Instant::now();
        let mut wait = ReleaseWait::new(t0);
        assert_eq!(wait.step(true, t0), Release::Wait);
        assert_eq!(wait.step(false, t0 + ms(100)), Release::Wait);
        assert_eq!(wait.step(true, t0 + ms(120)), Release::Wait, "Alt bounced");
        assert_eq!(wait.step(false, t0 + ms(130)), Release::Wait);
        assert_eq!(wait.step(false, t0 + ms(130) + RELEASE_SETTLE), Release::Ready);
    }

    #[test]
    fn keys_held_too_long_give_up() {
        let t0 = Instant::now();
        let mut wait = ReleaseWait::new(t0);
        assert_eq!(wait.step(true, t0 + RELEASE_PATIENCE - ms(1)), Release::Wait);
        assert_eq!(wait.step(true, t0 + RELEASE_PATIENCE), Release::TimedOut);
        // Released in time, it never times out, however long it settles.
        let mut late = ReleaseWait::new(t0);
        assert_eq!(late.step(false, t0 + RELEASE_PATIENCE * 2), Release::Wait);
    }
}
