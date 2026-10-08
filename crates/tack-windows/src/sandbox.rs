//! Escaping an MSIX container. A program started from inside a packaged app
//! (a terminal or coding tool hosted by one) can land in that app's container
//! without a package identity of its own. There Windows redirects AppData
//! writes and namespaces named objects, so Tack would run a stale private
//! copy of the board beside the user's real one. It relaunches itself
//! through the shell instead, and the contained copy exits.

use std::collections::HashMap;

use windows::core::PWSTR;
use windows::Win32::Foundation::{CloseHandle, APPMODEL_ERROR_NO_PACKAGE, ERROR_INSUFFICIENT_BUFFER};
use windows::Win32::Storage::Packaging::Appx::{GetCurrentPackageFullName, GetPackageFullName};
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
};
use windows::Win32::System::Threading::{OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION};

/// Set before relaunching. Explorer hands the launch to the running shell,
/// whose environment does not have it, so a relaunched Tack that still sees
/// it never escaped; it then carries on instead of relaunching forever.
const RELAUNCHED: &str = "TACK_RELAUNCHED_FROM_PACKAGE";

/// Lets a developer keep Tack attached to the console it was started from,
/// accepting the redirected AppData.
const STAY: &str = "TACK_STAY_IN_PACKAGE";

/// How far up the parent chain to look. Hosts sit a few levels up (app,
/// helper, shell, Tack); the chain ends long before this.
const MAX_ANCESTORS: usize = 16;

/// The package Tack is running under or below, if any: its own identity, or
/// that of the nearest packaged ancestor process.
pub fn host_package() -> Option<String> {
    if let Some(own) = own_package() {
        return Some(own);
    }
    let parents = parent_map();
    let mut pid = std::process::id();
    for _ in 0..MAX_ANCESTORS {
        let parent = *parents.get(&pid)?;
        // A parent id can be reused by an unrelated process that started
        // later; the walk ends at the root or at a cycle either way.
        if parent == 0 || parent == pid {
            return None;
        }
        if let Some(package) = package_of(parent) {
            return Some(package);
        }
        pid = parent;
    }
    None
}

/// Starts this exe again through Explorer, outside the container. False when
/// that was already tried, the developer opted out, or it could not start;
/// Tack then runs where it is.
pub fn relaunch_outside(host: &str) -> bool {
    if std::env::var_os(RELAUNCHED).is_some() || std::env::var_os(STAY).is_some() {
        return false;
    }
    let Ok(exe) = std::env::current_exe() else { return false };
    eprintln!("tack: started inside {host}'s container; starting again outside it (set {STAY}=1 to stay)");
    std::env::set_var(RELAUNCHED, "1");
    std::process::Command::new("explorer.exe").arg(exe).spawn().is_ok()
}

fn own_package() -> Option<String> {
    let mut length = 0u32;
    let rc = unsafe { GetCurrentPackageFullName(&mut length, None) };
    if rc == APPMODEL_ERROR_NO_PACKAGE || rc != ERROR_INSUFFICIENT_BUFFER {
        return None;
    }
    let mut buffer = vec![0u16; length as usize];
    let rc = unsafe { GetCurrentPackageFullName(&mut length, Some(PWSTR(buffer.as_mut_ptr()))) };
    rc.is_ok().then(|| from_wide(&buffer))
}

fn package_of(pid: u32) -> Option<String> {
    let process = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) }.ok()?;
    let mut length = 0u32;
    let mut name = None;
    let rc = unsafe { GetPackageFullName(process, &mut length, None) };
    if rc == ERROR_INSUFFICIENT_BUFFER {
        let mut buffer = vec![0u16; length as usize];
        let rc = unsafe { GetPackageFullName(process, &mut length, Some(PWSTR(buffer.as_mut_ptr()))) };
        if rc.is_ok() {
            name = Some(from_wide(&buffer));
        }
    }
    unsafe {
        let _ = CloseHandle(process);
    }
    name
}

/// Every running process's parent, from one snapshot.
fn parent_map() -> HashMap<u32, u32> {
    let mut parents = HashMap::new();
    let Ok(snapshot) = (unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) }) else { return parents };
    let mut entry = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
    let mut more = unsafe { Process32FirstW(snapshot, &mut entry) }.is_ok();
    while more {
        parents.insert(entry.th32ProcessID, entry.th32ParentProcessID);
        more = unsafe { Process32NextW(snapshot, &mut entry) }.is_ok();
    }
    unsafe {
        let _ = CloseHandle(snapshot);
    }
    parents
}

fn from_wide(buffer: &[u16]) -> String {
    let end = buffer.iter().position(|&c| c == 0).unwrap_or(buffer.len());
    String::from_utf16_lossy(&buffer[..end])
}
