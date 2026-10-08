//! Pinning the selection (Win+Alt+C): Tack asks the app in front to copy
//! what is selected, reads the copy, and puts the user's clipboard back as
//! it was. Before sending Ctrl+C it waits for the shortcut's own keys to be
//! let go, so the app does not read Win+Alt+Ctrl+C.
//!
//! The keys go only to the window that was in front when the shortcut
//! fired ([`Target`]), only that app's copy is read, and the user's
//! clipboard is put back only if nobody else wrote to it meanwhile.

use std::path::PathBuf;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::{Duration, Instant};

use image::DynamicImage;
use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{HINSTANCE, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Input::KeyboardAndMouse::{
    MapVirtualKeyW, SendInput, INPUT, INPUT_0, INPUT_KEYBOARD, KEYBDINPUT, KEYBD_EVENT_FLAGS, KEYEVENTF_EXTENDEDKEY,
    KEYEVENTF_KEYUP, MAPVK_VK_TO_VSC, VIRTUAL_KEY, VK_C, VK_CONTROL, VK_INSERT, VK_LWIN, VK_MENU, VK_RWIN, VK_SHIFT,
};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, FindWindowExW, GetForegroundWindow, RegisterClassW, HWND_MESSAGE,
    WINDOW_EX_STYLE, WINDOW_STYLE, WNDCLASSW,
};

use crate::clipboard::{self, Content, Restored};
use crate::focus::{class_name, window_process};
use crate::pointer;

/// How long the app in front gets to copy after Ctrl+C.
const COPY_PATIENCE: Duration = Duration::from_millis(400);
/// How long to wait for the shortcut's keys to be let go.
const RELEASE_PATIENCE: Duration = Duration::from_millis(1500);
/// The keys must stay up this long before Ctrl+C goes out.
const RELEASE_SETTLE: Duration = Duration::from_millis(30);
/// A key code Windows leaves unassigned: pressing it does nothing anywhere,
/// but it counts as "another key" while Alt or Win is down.
const MASK_KEY: u16 = 0xE8;
const POLL: Duration = Duration::from_millis(10);
/// How long the snipping listener goes on ignoring the clipboard after a
/// grab: a last clipboard event may still be on its way to it.
const GRAB_TAIL: Duration = Duration::from_millis(250);

/// Terminals, by window class. Ctrl+C there interrupts the running command,
/// so they get Ctrl+Insert, which copies the selection in all of them.
const TERMINALS: [&str; 6] = [
    "ConsoleWindowClass",            // the console (conhost)
    "CASCADIA_HOSTING_WINDOW_CLASS", // Windows Terminal
    "mintty",                        // Git Bash, Cygwin, MSYS2
    "VirtualConsoleClass",           // ConEmu, Cmder
    "PuTTY",
    "org.wezfurlong.wezterm",
];

/// How many grabs (and their tails) are running, so the snipping listener
/// ignores the clipboard changes they cause.
static GRABBING: AtomicUsize = AtomicUsize::new(0);

/// Whether a grab is changing the clipboard right now.
pub(crate) fn grabbing() -> bool {
    GRABBING.load(Ordering::SeqCst) > 0
}

/// One running grab in [`GRABBING`]. It counts until [`GRAB_TAIL`] after
/// it is dropped, and only for itself, so an earlier grab's tail never
/// ends a later grab's count.
struct Grabbing;

impl Grabbing {
    fn start() -> Grabbing {
        GRABBING.fetch_add(1, Ordering::SeqCst);
        Grabbing
    }
}

impl Drop for Grabbing {
    fn drop(&mut self) {
        let tail = std::thread::Builder::new().name("tack-grab-tail".into()).spawn(|| {
            std::thread::sleep(GRAB_TAIL);
            GRABBING.fetch_sub(1, Ordering::SeqCst);
        });
        if tail.is_err() {
            GRABBING.fetch_sub(1, Ordering::SeqCst);
        }
    }
}

/// The window the pin shortcut was pressed over, taken the moment it fired.
#[derive(Clone, Copy, Debug)]
pub struct Target {
    /// Its handle, as a number so a target can cross threads.
    hwnd: isize,
    pid: u32,
    /// For a Store app's frame (ApplicationFrameHost), the process of the
    /// app inside it, which is the one that copies.
    hosted: Option<u32>,
    terminal: bool,
}

impl Target {
    /// The window in front now; call on the hotkey thread as the shortcut
    /// fires, before the user can switch away.
    pub fn in_front() -> Option<Target> {
        let hwnd = unsafe { GetForegroundWindow() };
        let pid = window_process(hwnd)?;
        let class = class_name(hwnd);
        let hosted = (class == "ApplicationFrameWindow")
            .then(|| unsafe { FindWindowExW(Some(hwnd), None, w!("Windows.UI.Core.CoreWindow"), PCWSTR::null()) })
            .and_then(|core| core.ok())
            .and_then(window_process);
        Some(Target { hwnd: hwnd.0 as isize, pid, hosted, terminal: TERMINALS.contains(&class.as_str()) })
    }

    /// The process the window belongs to.
    pub fn pid(&self) -> u32 {
        self.pid
    }

    fn hwnd(&self) -> HWND {
        HWND(self.hwnd as *mut _)
    }

    /// Still the window in front.
    fn still_in_front(&self) -> bool {
        (unsafe { GetForegroundWindow() }) == self.hwnd()
    }

    /// Whether the clipboard's latest write is this app's. An app that wrote
    /// without naming a window counts if the target is still in front, as
    /// the keys went to it a moment ago.
    fn wrote_clipboard(&self) -> bool {
        match clipboard::owner() {
            Some(owner) => {
                owner == self.hwnd()
                    || window_process(owner).is_some_and(|pid| pid == self.pid || Some(pid) == self.hosted)
            }
            None => self.still_in_front(),
        }
    }
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

/// What became of the user's clipboard.
pub enum Clipboard {
    /// Never changed by the grab: nothing to put back.
    Untouched,
    /// Put back as it was.
    Restored,
    /// Another app wrote to it after the copy: theirs stays.
    LeftNewer,
    /// Not put back, or only in part.
    Failed(String),
}

/// A grab, and what became of the user's clipboard.
pub struct Grab {
    pub what: Grabbed,
    pub clipboard: Clipboard,
}

impl Grab {
    fn nothing() -> Grab {
        Grab { what: Grabbed::Nothing, clipboard: Clipboard::Untouched }
    }
}

/// Call the moment the shortcut fires (on the hotkey thread): while Alt or
/// Win is held, a press of an unassigned key keeps their release from
/// opening the menu bar of the app in front, or Start.
pub fn mask_menu() {
    let held = [VK_MENU, VK_LWIN, VK_RWIN].iter().any(|vk| pointer::key_down(vk.0));
    if held {
        send(&[key(MASK_KEY, false), key(MASK_KEY, true)]);
    }
}

/// Waits for the shortcut's keys to be let go, then copies the selection in
/// `target` (the window in front when the shortcut fired), reads it and
/// restores the clipboard. Nothing is sent if another window has come to
/// the front meanwhile. Blocks for up to about two seconds; call it on a
/// thread of its own, never the UI thread.
pub fn grab(target: Option<Target>) -> Grab {
    let mut wait = ReleaseWait::new(Instant::now());
    loop {
        match wait.step(modifiers_down(), Instant::now()) {
            Release::Ready => break,
            Release::Wait => std::thread::sleep(POLL),
            Release::TimedOut => return Grab { what: Grabbed::KeysHeld, clipboard: Clipboard::Untouched },
        }
    }
    let Some(target) = target else {
        log("no window was in front");
        return Grab::nothing();
    };
    let Some(owner) = Owner::new() else { return Grab::nothing() };
    let _grabbing = Grabbing::start();
    copy_and_read(owner.0, target)
}

fn copy_and_read(owner: HWND, target: Target) -> Grab {
    let before = clipboard::snapshot(owner);
    let mut sequence = clipboard::sequence();
    if !target.still_in_front() {
        log("another window came to the front; no keys sent");
        return Grab::nothing();
    }
    let copy = if target.terminal {
        // Ctrl+Insert: Insert is an extended key, told apart from the
        // keypad's 0 by its flag.
        [key(VK_CONTROL.0, false), extended(VK_INSERT.0, false), extended(VK_INSERT.0, true), key(VK_CONTROL.0, true)]
    } else {
        [key(VK_CONTROL.0, false), key(VK_C.0, false), key(VK_C.0, true), key(VK_CONTROL.0, true)]
    };
    if !send(&copy) {
        log("SendInput refused the keys");
        return Grab::nothing();
    }
    let give_up = Instant::now() + COPY_PATIENCE;
    loop {
        let now = clipboard::sequence();
        if now != sequence {
            sequence = now;
            if target.wrote_clipboard() {
                break;
            }
            log("another app wrote to the clipboard; still waiting for the copy");
        }
        if Instant::now() >= give_up {
            log("the app in front did not copy anything");
            return Grab::nothing();
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
    let read = clipboard::sequence();
    if read != sequence && !target.wrote_clipboard() {
        // What was read may be another app's; the clipboard is theirs now.
        log("another app wrote to the clipboard while the copy was read");
        return Grab { what: Grabbed::Nothing, clipboard: Clipboard::LeftNewer };
    }
    let clipboard = match &before {
        Some(snapshot) => match clipboard::restore(owner, snapshot, read) {
            Ok(Restored::Done) => Clipboard::Restored,
            Ok(Restored::Superseded) => Clipboard::LeftNewer,
            Err(e) => Clipboard::Failed(e),
        },
        None => Clipboard::Failed("the clipboard could not be read before the copy".into()),
    };
    Grab { what, clipboard }
}

/// Debug builds say why a grab found nothing.
fn log(why: &str) {
    if cfg!(debug_assertions) {
        eprintln!("tack: pin selection: {why}");
    }
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

/// An extended key (Insert, the arrows...), with its scan code, which some
/// terminals read instead of the key code.
fn extended(vk: u16, up: bool) -> INPUT {
    let mut input = key(vk, up);
    let ki = unsafe { &mut input.Anonymous.ki };
    ki.wScan = unsafe { MapVirtualKeyW(vk as u32, MAPVK_VK_TO_VSC) } as u16;
    ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
    input
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
enum Release {
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
struct ReleaseWait {
    started: Instant,
    up_since: Option<Instant>,
}

impl ReleaseWait {
    fn new(now: Instant) -> ReleaseWait {
        ReleaseWait { started: now, up_since: None }
    }

    /// `held`: any modifier is down at `now`.
    fn step(&mut self, held: bool, now: Instant) -> Release {
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
