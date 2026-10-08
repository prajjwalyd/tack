//! Small file helpers shared by the board and the integrations: comparing
//! Windows paths, telling an edit from a stray event, and moving a file to a
//! free name.

use std::path::{Path, PathBuf};
use std::time::SystemTime;

/// Windows paths are case-insensitive.
pub fn path_key(path: &Path) -> String {
    path.to_string_lossy().to_lowercase()
}

pub fn file_name(path: &Path) -> String {
    path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
}

/// Modification time and size, to tell a real edit from a repeated event.
pub fn file_stamp(path: &Path) -> Option<(SystemTime, u64)> {
    let meta = std::fs::metadata(path).ok()?;
    Some((meta.modified().ok()?, meta.len()))
}

/// The screenshot formats Tack pins.
pub fn is_image(path: &Path) -> bool {
    path.extension()
        .and_then(|e| e.to_str())
        .map(|e| matches!(e.to_ascii_lowercase().as_str(), "png" | "jpg" | "jpeg"))
        .unwrap_or(false)
}

/// The file's own name in `dir`, or "name (2).png" and so on if it is taken.
pub fn free_name(dir: &Path, file: &Path) -> PathBuf {
    let stem = file.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
    let ext = file.extension().map(|s| s.to_string_lossy().into_owned()).unwrap_or_else(|| "png".into());
    (1..)
        .map(|n| if n == 1 { dir.join(format!("{stem}.{ext}")) } else { dir.join(format!("{stem} ({n}).{ext}")) })
        .find(|p| !p.exists())
        .expect("a free name")
}

/// A rename, or a copy when the folder is on another drive.
pub fn move_file(from: &Path, to: &Path) -> std::io::Result<()> {
    if std::fs::rename(from, to).is_ok() {
        return Ok(());
    }
    std::fs::copy(from, to)?;
    std::fs::remove_file(from)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn path_keys_ignore_case() {
        assert_eq!(path_key(Path::new(r"C:\Pics\A.PNG")), path_key(Path::new(r"c:\pics\a.png")));
    }

    #[test]
    fn only_screenshot_formats_are_images() {
        assert!(is_image(Path::new("a.PNG")));
        assert!(is_image(Path::new("a.jpeg")));
        assert!(!is_image(Path::new("a.gif")));
        assert!(!is_image(Path::new("png")));
    }
}
