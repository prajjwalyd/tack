//! This device's NetBird status, from the client's command-line tool
//! (`netbird status --json`), which talks to the NetBird service for us and
//! needs no admin rights. Parsing lives in `tack_core::netbird`.

use std::ffi::c_void;
use std::io::Read;
use std::os::windows::process::CommandExt;
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::sync::mpsc;
use std::time::Duration;

use tack_core::netbird::{self, Status};
use windows::Win32::Foundation::{HANDLE, NO_ERROR};
use windows::Win32::NetworkManagement::IpHelper::{
    CancelMibChangeNotify2, NotifyIpInterfaceChange, MIB_IPINTERFACE_ROW, MIB_NOTIFICATION_TYPE,
};
use windows::Win32::Networking::WinSock::AF_UNSPEC;
use windows::Win32::System::Com::CoTaskMemFree;
use windows::Win32::UI::Shell::{FOLDERID_ProgramFiles, SHGetKnownFolderPath, KNOWN_FOLDER_FLAG};

/// No console window flashes up while the tool runs.
const CREATE_NO_WINDOW: u32 = 0x0800_0000;
/// The tool answers in well under a second; one that hangs (its service
/// restarting, say) is given up on rather than holding up the caller.
const PATIENCE: Duration = Duration::from_secs(5);
/// More than any real status, so a misbehaving tool cannot fill memory.
const MAX_OUTPUT: u64 = 4 * 1024 * 1024;

/// This device's NetBird status, or None when NetBird is not installed, not
/// running, not connected, or too slow to answer.
pub fn status() -> Option<Status> {
    let mut child = Command::new(cli()?)
        .args(["status", "--json"])
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .creation_flags(CREATE_NO_WINDOW)
        .spawn()
        .ok()?;
    // Read while it runs (a full pipe would stall it), and stop waiting at
    // the deadline.
    let mut stdout = child.stdout.take()?;
    let (sent, got) = mpsc::channel();
    let reader = std::thread::Builder::new().name("tack-netbird".into()).spawn(move || {
        let mut out = Vec::new();
        let read = (&mut stdout).take(MAX_OUTPUT).read_to_end(&mut out);
        let _ = sent.send(read.map(|_| out));
    });
    let out = match reader {
        Ok(_) => got.recv_timeout(PATIENCE).ok().and_then(Result::ok),
        Err(_) => None,
    };
    let Some(out) = out else {
        let _ = child.kill();
        let _ = child.wait();
        return None;
    };
    if !child.wait().ok()?.success() {
        return None;
    }
    netbird::parse(&String::from_utf8_lossy(&out))
}

/// True when the NetBird client is installed, connected or not.
pub fn installed() -> bool {
    cli().is_some()
}

/// The NetBird tool where its installer puts it, and only there: Program
/// Files (asked of the shell, not read from environment variables, which
/// the user's own programs can change) is writable by administrators alone,
/// so no program can plant a fake `netbird.exe` there to lie about the
/// peers. The tool in turn checks it talks to the real NetBird service.
fn cli() -> Option<PathBuf> {
    let program_files = unsafe {
        let p = SHGetKnownFolderPath(&FOLDERID_ProgramFiles, KNOWN_FOLDER_FLAG(0), None).ok()?;
        let path = PathBuf::from(p.to_string().unwrap_or_default());
        CoTaskMemFree(Some(p.0 as *const _));
        path
    };
    if program_files.as_os_str().is_empty() {
        return None;
    }
    Some(program_files.join("NetBird").join("netbird.exe")).filter(|p| p.is_file())
}

/// Calls a function whenever a network interface appears, goes or changes
/// (NetBird connecting or disconnecting shows up as one), until dropped. The
/// call comes from a Windows thread-pool thread, so it should do no more
/// than wake someone.
pub struct NetworkWatch(HANDLE);

impl NetworkWatch {
    /// None if Windows would not start the watch.
    pub fn start(changed: fn()) -> Option<NetworkWatch> {
        let mut handle = HANDLE::default();
        let context = changed as usize as *const c_void;
        let started =
            unsafe { NotifyIpInterfaceChange(AF_UNSPEC, Some(interface_changed), Some(context), false, &mut handle) };
        (started == NO_ERROR).then_some(NetworkWatch(handle))
    }
}

impl Drop for NetworkWatch {
    fn drop(&mut self) {
        // Returns once any call in progress is done.
        let _ = unsafe { CancelMibChangeNotify2(self.0) };
    }
}

unsafe extern "system" fn interface_changed(
    context: *const c_void,
    _row: *const MIB_IPINTERFACE_ROW,
    _kind: MIB_NOTIFICATION_TYPE,
) {
    // The `fn()` that `start` was given.
    let changed: fn() = unsafe { std::mem::transmute(context) };
    changed();
}
