//! Trimming working sets once the web view is asleep, so Tack's RAM use
//! drops right after a tuck instead of whenever the runtime gets to it. Only
//! RAM is handed back (pages go to the standby list, from where a reveal
//! takes them straight back); private bytes do not change.

use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;

use webview2_com::Microsoft::Web::WebView2::Win32::{ICoreWebView2, ICoreWebView2Environment8, ICoreWebView2_2};
use windows::core::Interface;
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::System::Memory::SetProcessWorkingSetSizeEx;
use windows::Win32::System::Threading::{
    GetCurrentProcess, OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SET_QUOTA,
};

/// How long after the suspend the WebView2 processes are trimmed again.
const SECOND_TRIM: Duration = Duration::from_secs(4);

/// Counts wakes, so a delayed trim can tell the board has been revealed
/// since it was planned.
pub(super) static WAKES: AtomicU64 = AtomicU64::new(0);

/// Trims tack.exe's own working set: what startup and a reveal touched
/// (image decoding, the window code) is not needed until the next one.
pub(super) fn own_working_set() {
    // Both sizes at usize::MAX means "remove as many pages as possible".
    let _ = unsafe { SetProcessWorkingSetSizeEx(GetCurrentProcess(), usize::MAX, usize::MAX, Default::default()) };
}

/// Trims the WebView2 processes' working sets, now and once more after
/// [`SECOND_TRIM`] unless the board is revealed meanwhile: the GPU and
/// browser processes keep tidying up for a few seconds after the suspend and
/// touch some pages again.
pub(super) fn web_view_processes(webview: &ICoreWebView2) {
    // Debug builds: TACK_DEBUG_TRIM=off leaves it to the runtime, to compare.
    if cfg!(debug_assertions) && std::env::var("TACK_DEBUG_TRIM").as_deref() == Ok("off") {
        return;
    }
    let processes = processes(webview);
    for p in &processes {
        p.trim();
    }
    let wakes = WAKES.load(Ordering::SeqCst);
    std::thread::spawn(move || {
        std::thread::sleep(SECOND_TRIM);
        if WAKES.load(Ordering::SeqCst) == wakes {
            for p in &processes {
                p.trim();
            }
            trace!("web view processes trimmed again");
        }
    });
}

/// A WebView2 process, held open (so its id cannot be reused) until dropped.
struct Process(HANDLE);

// A process handle may be used, and closed, from any thread.
unsafe impl Send for Process {}

impl Process {
    fn trim(&self) {
        let _ = unsafe { SetProcessWorkingSetSizeEx(self.0, usize::MAX, usize::MAX, Default::default()) };
    }
}

impl Drop for Process {
    fn drop(&mut self) {
        let _ = unsafe { CloseHandle(self.0) };
    }
}

/// The web view's processes (browser, GPU, renderer, utilities), as the
/// runtime lists them (`GetProcessInfos`, runtime 1.0.1072 or later).
fn processes(webview: &ICoreWebView2) -> Vec<Process> {
    let mut processes = Vec::new();
    let Ok(webview2) = webview.cast::<ICoreWebView2_2>() else { return processes };
    let Ok(env) = (unsafe { webview2.Environment() }) else { return processes };
    let Ok(env8) = env.cast::<ICoreWebView2Environment8>() else { return processes };
    let Ok(infos) = (unsafe { env8.GetProcessInfos() }) else { return processes };
    let mut count = 0;
    let _ = unsafe { infos.Count(&mut count) };
    for i in 0..count {
        let Ok(info) = (unsafe { infos.GetValueAtIndex(i) }) else { continue };
        let mut pid = 0;
        if unsafe { info.ProcessId(&mut pid) }.is_err() {
            continue;
        }
        let access = PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION;
        if let Ok(handle) = unsafe { OpenProcess(access, false, pid as u32) } {
            processes.push(Process(handle));
        }
    }
    processes
}
