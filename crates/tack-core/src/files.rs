//! Small file helpers shared by the board and the integrations: comparing
//! Windows paths, telling an edit from a stray event, and moving a file to a
//! free name without ever replacing another.

use std::fs::{File, OpenOptions};
use std::io::ErrorKind;
use std::path::{Component, Path, PathBuf};
use std::time::SystemTime;

/// Windows paths are case-insensitive.
pub fn path_key(path: &Path) -> String {
    path.to_string_lossy().to_lowercase()
}

/// Both name the same folder: the same text or, failing that, the same
/// folder once resolved, so an 8.3 short name, a `\\?\` prefix or a
/// trailing separator does not make them look different.
pub fn same_dir(a: &Path, b: &Path) -> bool {
    if path_key(a) == path_key(b) {
        return true;
    }
    match (std::fs::canonicalize(a), std::fs::canonicalize(b)) {
        (Ok(a), Ok(b)) => path_key(&a) == path_key(&b),
        _ => false,
    }
}

/// A path to a file and nothing more: a file name, and no `.` or `..`
/// anywhere that could make it point outside the folder it names.
pub fn is_plain(path: &Path) -> bool {
    path.file_name().is_some()
        && path.components().all(|c| matches!(c, Component::Prefix(_) | Component::RootDir | Component::Normal(_)))
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

/// Far more names than any real folder needs; past it, something is wrong.
const MAX_NAMES: u32 = 10_000;

/// Moves `from` into `dir` under its own name, or "name (2).png" and so on,
/// never over a file that is already there, even one that turns up while
/// the move runs. `claim` hears each name just before it is tried and may
/// call the move off (false). Returns where the file went, or `None` if
/// `claim` called it off.
pub fn move_to_free_name(
    from: &Path,
    dir: &Path,
    mut claim: impl FnMut(&Path) -> bool,
) -> std::io::Result<Option<PathBuf>> {
    let stem = from.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
    let ext = from.extension().map(|s| s.to_string_lossy().into_owned()).unwrap_or_else(|| "png".into());
    for n in 1..=MAX_NAMES {
        let to = if n == 1 { dir.join(format!("{stem}.{ext}")) } else { dir.join(format!("{stem} ({n}).{ext}")) };
        if std::fs::symlink_metadata(&to).is_ok() {
            continue;
        }
        if !claim(&to) {
            return Ok(None);
        }
        match move_new(from, &to) {
            Ok(()) => return Ok(Some(to)),
            // Taken since the look above: on to the next name.
            Err(e) if e.kind() == ErrorKind::AlreadyExists => continue,
            Err(e) => return Err(e),
        }
    }
    Err(std::io::Error::other(format!("no free name for {stem}.{ext}")))
}

/// Moves `from` to `to`, failing with `AlreadyExists` rather than replace
/// anything there (a plain rename on Windows replaces). All or nothing: on
/// failure `from` is where it was and nothing is left at `to`.
pub fn move_new(from: &Path, to: &Path) -> std::io::Result<()> {
    // A hard link claims the new name atomically, on the same drive.
    match std::fs::hard_link(from, to) {
        Ok(()) => {
            if let Err(e) = std::fs::remove_file(from) {
                // Two names for one file: drop the new one, the data stays.
                let _ = std::fs::remove_file(to);
                return Err(e);
            }
            Ok(())
        }
        Err(e) if e.kind() == ErrorKind::AlreadyExists => Err(e),
        // Another drive, or no hard links there: copy into a claimed file.
        Err(_) => copy_new(from, to),
    }
}

fn copy_new(from: &Path, to: &Path) -> std::io::Result<()> {
    let mut dest = OpenOptions::new().write(true).create_new(true).open(to)?;
    let copied = File::open(from).and_then(|mut src| std::io::copy(&mut src, &mut dest)).and_then(|_| dest.sync_all());
    drop(dest);
    if let Err(e) = copied.and_then(|()| std::fs::remove_file(from)) {
        let _ = std::fs::remove_file(to);
        return Err(e);
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("tack-files-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

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

    #[test]
    fn plain_paths_have_no_dots() {
        assert!(is_plain(Path::new(r"C:\Tack\Captures\a.png")));
        assert!(!is_plain(Path::new(r"C:\Tack\Captures\..\a.png")));
        assert!(!is_plain(Path::new(r"..\a.png")));
        assert!(!is_plain(Path::new(r"C:\Tack\Captures\..")));
        assert!(!is_plain(Path::new(r"C:\")));
    }

    #[test]
    fn the_same_folder_spelled_another_way_is_the_same() {
        let dir = scratch("same-dir");
        let trailing = PathBuf::from(format!("{}{}", dir.display(), std::path::MAIN_SEPARATOR));
        let resolved = std::fs::canonicalize(&dir).unwrap();
        let other = dir.join("sub");
        std::fs::create_dir_all(&other).unwrap();
        let same = (same_dir(&dir, &trailing), same_dir(&dir, &resolved), same_dir(&dir, &other));
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(same, (true, true, false));
    }

    #[test]
    fn a_move_never_replaces_a_file_and_takes_the_next_free_name() {
        let dir = scratch("move");
        let from_dir = dir.join("Captures");
        let to_dir = dir.join("Screenshots");
        std::fs::create_dir_all(&from_dir).unwrap();
        std::fs::create_dir_all(&to_dir).unwrap();
        let from = from_dir.join("Shot.png");
        std::fs::write(&from, b"capture").unwrap();
        std::fs::write(to_dir.join("Shot.png"), b"the user's own").unwrap();
        let mut tried = Vec::new();
        let moved = move_to_free_name(&from, &to_dir, |to| {
            tried.push(file_name(to));
            // Another app takes the name between the look and the move.
            if tried.len() == 1 {
                std::fs::write(to, b"arrived meanwhile").unwrap();
            }
            true
        })
        .unwrap();
        let first = std::fs::read(to_dir.join("Shot.png")).unwrap();
        let theirs = std::fs::read(to_dir.join("Shot (2).png")).unwrap();
        let ours = std::fs::read(to_dir.join("Shot (3).png")).unwrap();
        let source_left = from.exists();
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(tried, ["Shot (2).png", "Shot (3).png"]);
        assert_eq!(moved, Some(to_dir.join("Shot (3).png")));
        assert_eq!(first, b"the user's own");
        assert_eq!(theirs, b"arrived meanwhile");
        assert_eq!(ours, b"capture");
        assert!(!source_left);
    }

    #[test]
    fn a_called_off_move_leaves_the_file() {
        let dir = scratch("called-off");
        let from = dir.join("a.png");
        std::fs::write(&from, b"x").unwrap();
        let target = dir.join("out");
        std::fs::create_dir_all(&target).unwrap();
        let moved = move_to_free_name(&from, &target, |_| false).unwrap();
        let still = from.exists();
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(moved, None);
        assert!(still);
    }
}
