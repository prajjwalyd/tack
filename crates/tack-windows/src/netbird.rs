//! This device's NetBird status, from the client's command-line tool
//! (`netbird status --json`), which talks to the NetBird service for us and
//! needs no admin rights. Parsing lives in `tack_core::netbird`.

use std::io::Read;
use std::os::windows::process::CommandExt;
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::sync::mpsc;
use std::time::Duration;

use tack_core::netbird::{self, Status};
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
