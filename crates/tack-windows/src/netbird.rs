//! This device's NetBird status, from the client's command-line tool
//! (`netbird status --json`), which talks to the NetBird service for us and
//! needs no admin rights. Parsing lives in `tack_core::netbird`.

use std::os::windows::process::CommandExt;
use std::path::PathBuf;
use std::process::{Command, Stdio};

use tack_core::netbird::{self, Status};

/// No console window flashes up while the tool runs.
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// This device's NetBird status, or None when NetBird is not installed, not
/// running or not connected.
pub fn status() -> Option<Status> {
    let output = Command::new(cli()?)
        .args(["status", "--json"])
        .stdin(Stdio::null())
        .stderr(Stdio::null())
        .creation_flags(CREATE_NO_WINDOW)
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }
    netbird::parse(&String::from_utf8_lossy(&output.stdout))
}

/// True when the NetBird client is installed, connected or not.
pub fn installed() -> bool {
    cli().is_some()
}

/// The NetBird tool where its installer puts it, and only there: Program
/// Files is writable by administrators alone, while a folder on the PATH may
/// be the user's own, where any program could plant a fake `netbird.exe` to
/// lie about the peers. The tool in turn checks it talks to the real NetBird
/// service.
fn cli() -> Option<PathBuf> {
    let program_files = std::env::var_os("ProgramW6432").or_else(|| std::env::var_os("ProgramFiles"))?;
    Some(PathBuf::from(program_files).join("NetBird").join("netbird.exe")).filter(|p| p.is_file())
}
