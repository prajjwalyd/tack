//! Tack's global shortcuts: one shows or hides the board (Win+Alt+S by
//! default), the other pins the current selection (Win+Alt+C). Both can be
//! changed at any time, turned off, or paused while the Shortcuts dialog
//! listens for a new chord.
//!
//! `RegisterHotKey` binds a shortcut to the thread that registers it, so one
//! thread of its own owns them all: it registers, unregisters and hears
//! `WM_HOTKEY` in its message loop, and takes requests from other threads
//! through a channel, woken by a posted message.

use std::sync::mpsc::{self, Receiver, Sender};

use tack_core::shortcut::Chord;
use windows::Win32::Foundation::{LPARAM, WPARAM};
use windows::Win32::System::Threading::GetCurrentThreadId;
use windows::Win32::UI::Input::KeyboardAndMouse::{
    RegisterHotKey, UnregisterHotKey, HOT_KEY_MODIFIERS, MOD_ALT, MOD_CONTROL, MOD_NOREPEAT, MOD_SHIFT, MOD_WIN,
};
use windows::Win32::UI::WindowsAndMessaging::{
    GetMessageW, PeekMessageW, PostThreadMessageW, MSG, PM_NOREMOVE, WM_APP, WM_HOTKEY, WM_USER,
};

/// What a shortcut does.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Action {
    /// Show or hide the board.
    Toggle,
    /// Pin the current selection.
    PinSelection,
}

const ACTIONS: [Action; 2] = [Action::Toggle, Action::PinSelection];

impl Action {
    fn id(self) -> i32 {
        match self {
            Action::Toggle => 1,
            Action::PinSelection => 2,
        }
    }
}

/// How registering one shortcut went.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Status {
    /// Registered: it works.
    Ok,
    /// Turned off.
    Off,
    /// Another app has it (or Windows keeps it for itself).
    InUse,
}

/// Wakes the hotkey thread to look at its requests.
const WM_REQUEST: u32 = WM_APP + 7;

enum Request {
    /// Register these chords in place of the current ones.
    Set([Option<Chord>; 2], Sender<[Status; 2]>),
    /// Unregister everything for now (true), or register it all again.
    Pause(bool, Sender<[Status; 2]>),
}

/// The running hotkey thread.
pub struct Hotkeys {
    requests: Sender<Request>,
    thread: u32,
}

/// Starts the hotkey thread with no shortcuts; [`Hotkeys::set`] registers
/// them. `on_press` runs on that thread for every press, so it should only
/// hand the work on.
pub fn start(on_press: impl Fn(Action) + Send + 'static) -> Option<Hotkeys> {
    let (requests, inbox) = mpsc::channel::<Request>();
    let (ready, thread_id) = mpsc::channel::<u32>();
    let spawned = std::thread::Builder::new().name("tack-hotkey".into()).spawn(move || {
        let mut msg = MSG::default();
        // A thread has no message queue until it asks for one; make it now so
        // requests posted before the first GetMessage are not lost.
        unsafe {
            let _ = PeekMessageW(&mut msg, None, WM_USER, WM_USER, PM_NOREMOVE);
        }
        let _ = ready.send(unsafe { GetCurrentThreadId() });
        run(on_press, inbox);
    });
    if let Err(e) = spawned {
        eprintln!("tack: cannot start the shortcuts thread: {e}");
        return None;
    }
    let thread = thread_id.recv().ok()?;
    Some(Hotkeys { requests, thread })
}

impl Hotkeys {
    /// Registers `toggle` and `pin` (`None`: off) in place of whatever was
    /// registered, and says how each went.
    pub fn set(&self, toggle: Option<Chord>, pin: Option<Chord>) -> [Status; 2] {
        self.ask(|reply| Request::Set([toggle, pin], reply))
    }

    /// Unregisters the shortcuts while `paused` (so the Shortcuts dialog can
    /// hear any chord, Tack's own included), and registers them again after.
    pub fn pause(&self, paused: bool) -> [Status; 2] {
        self.ask(|reply| Request::Pause(paused, reply))
    }

    fn ask(&self, request: impl FnOnce(Sender<[Status; 2]>) -> Request) -> [Status; 2] {
        let (reply, answer) = mpsc::channel();
        let sent = self.requests.send(request(reply)).is_ok()
            && unsafe { PostThreadMessageW(self.thread, WM_REQUEST, WPARAM(0), LPARAM(0)) }.is_ok();
        if !sent {
            return [Status::InUse, Status::InUse];
        }
        answer.recv().unwrap_or([Status::InUse, Status::InUse])
    }
}

/// The hotkey thread's loop.
fn run(on_press: impl Fn(Action), inbox: Receiver<Request>) {
    let mut chords: [Option<Chord>; 2] = [None, None];
    let mut paused = false;
    let mut msg = MSG::default();
    while unsafe { GetMessageW(&mut msg, None, 0, 0) }.0 > 0 {
        if msg.message == WM_HOTKEY {
            if let Some(action) = ACTIONS.into_iter().find(|a| msg.wParam.0 == a.id() as usize) {
                on_press(action);
            }
        } else if msg.message == WM_REQUEST {
            while let Ok(request) = inbox.try_recv() {
                match request {
                    Request::Set(next, reply) => {
                        chords = next;
                        let _ = reply.send(if paused { statuses_while_paused(&chords) } else { register(&chords) });
                    }
                    Request::Pause(pause, reply) => {
                        paused = pause;
                        let statuses = if pause {
                            unregister_all();
                            statuses_while_paused(&chords)
                        } else {
                            register(&chords)
                        };
                        let _ = reply.send(statuses);
                    }
                }
            }
        }
    }
}

/// While paused nothing is registered; a chord counts as fine until it is
/// tried for real.
fn statuses_while_paused(chords: &[Option<Chord>; 2]) -> [Status; 2] {
    chords.map(|c| if c.is_some() { Status::Ok } else { Status::Off })
}

fn unregister_all() {
    for action in ACTIONS {
        unsafe {
            let _ = UnregisterHotKey(None, action.id());
        }
    }
}

/// Registers both chords afresh, on this (the hotkey) thread.
fn register(chords: &[Option<Chord>; 2]) -> [Status; 2] {
    unregister_all();
    let mut statuses = [Status::Off, Status::Off];
    for (i, action) in ACTIONS.into_iter().enumerate() {
        let Some(chord) = chords[i] else { continue };
        let result = unsafe { RegisterHotKey(None, action.id(), modifiers(&chord), chord.vk as u32) };
        statuses[i] = match result {
            Ok(()) => Status::Ok,
            Err(e) => {
                eprintln!("tack: cannot register {chord}: {e}");
                Status::InUse
            }
        };
    }
    statuses
}

fn modifiers(chord: &Chord) -> HOT_KEY_MODIFIERS {
    let mut mods = MOD_NOREPEAT;
    for (on, flag) in [(chord.win, MOD_WIN), (chord.ctrl, MOD_CONTROL), (chord.alt, MOD_ALT), (chord.shift, MOD_SHIFT)]
    {
        if on {
            mods |= flag;
        }
    }
    mods
}
