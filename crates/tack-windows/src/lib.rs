//! Tack's Windows integrations, one module per capability. Each one wraps the
//! Win32 (or shell) side of a feature behind plain functions and callbacks,
//! so this crate never depends on Tauri: `tack-app` wires the callbacks to
//! the board and the UI.
//!
//! Threads: the clipboard listener, the hotkeys and the pointer poller each
//! run on a thread of their own with their own message loop or timer;
//! pinning the selection blocks for a moment and runs on a worker. Window
//! operations ([`overlay`], [`focus`], [`drag_out`]) must run on the UI
//! thread, which is the caller's job.

pub mod autostart;
pub mod capture_origin;
mod clipboard;
pub mod drag_out;
pub mod edge_reveal;
pub mod focus;
pub mod hotkey;
pub mod netbird;
pub mod overlay;
pub mod pointer;
pub mod sandbox;
pub mod screenshots;
pub mod selection;
pub mod shell;
pub mod single_instance;
pub mod snipping_tool;

/// Window handles, as Tauri's `WebviewWindow::hwnd` hands them out.
pub use windows::Win32::Foundation::HWND;
