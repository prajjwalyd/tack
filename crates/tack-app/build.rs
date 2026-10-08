//! Generates the Tauri context (config, capabilities, embedded ui/ assets)
//! and the Windows resources (icon, manifest) for tack.exe.

fn main() {
    tauri_build::build()
}
