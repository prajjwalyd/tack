//! Captures: Snipping Tool images that only ever existed on the clipboard.
//! Tack saves each one as a PNG in its own folder so it can be dragged,
//! opened and edited like any screenshot file. When the print leaves the
//! board the file goes to the Recycle Bin (never deleted outright), since
//! the user never chose to keep that file but may still want it back.

use std::collections::HashSet;
use std::fs::{Metadata, OpenOptions};
use std::io::{Cursor, Write};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant, SystemTime};

use image::{DynamicImage, ImageFormat, RgbaImage};

use crate::files::{is_image, is_plain, path_key};
use crate::print::Print;
use crate::recycle::{recycle_file, Recycler};

/// One copy can change the clipboard several times (OLE sets it, then
/// flushes it); the same pixels again this soon are the same capture.
const REPEAT_WINDOW: Duration = Duration::from_secs(5);

/// Where captures are saved: `%LOCALAPPDATA%\Tack\Captures`.
pub fn captures_folder() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(|| PathBuf::from("."));
    base.join("Tack").join("Captures")
}

/// The file is one of Tack's own captures.
pub fn in_captures(path: &Path) -> bool {
    path.parent().is_some_and(|dir| path_key(dir) == path_key(&captures_folder()))
}

/// A file this young is never swept, pinned or not. Longer than the
/// history keeps an unkept print (7 days), so the sweep never judges a file
/// the board might still show: it only catches what a failed recycle, or a
/// crash before a save, left behind.
pub const SWEEP_GRACE: Duration = Duration::from_secs(8 * 24 * 60 * 60);

/// The newest modification time a file may have and still be swept at
/// `now`: older than [`SWEEP_GRACE`], and older than board.json's last
/// successful save (`board_saved`). A file written after that save may be
/// listed only in a later save that failed, so board.json cannot speak for
/// it.
pub fn sweep_cutoff(now: SystemTime, board_saved: Option<SystemTime>) -> SystemTime {
    let graced = now.checked_sub(SWEEP_GRACE).unwrap_or(SystemTime::UNIX_EPOCH);
    board_saved.map_or(graced, |saved| graced.min(saved))
}

/// Recycles the leftover captures in `folder`: images that no path in
/// `keep` names, and half-written ones, last modified before `cutoff` (see
/// [`sweep_cutoff`]). Returns the files that went.
///
/// Only call this when board.json was read and fully understood: if Tack
/// cannot say for sure which captures are pinned, every one of them would
/// look like a leftover. `keep` holds every path that board.json, or any
/// backup of it, mentions.
pub fn sweep(folder: &Path, keep: &[PathBuf], cutoff: SystemTime, bin: &dyn Recycler) -> Vec<PathBuf> {
    sweep_where(folder, keep, cutoff, bin, is_image)
}

/// [`sweep`] for any folder of Tack's own files: `ours` says which files in
/// it Tack wrote (half-written ones always count). Links and other reparse
/// points are never touched, and a folder that is one itself is not swept
/// at all: it may lead anywhere.
pub(crate) fn sweep_where(
    folder: &Path,
    keep: &[PathBuf],
    cutoff: SystemTime,
    bin: &dyn Recycler,
    ours: impl Fn(&Path) -> bool,
) -> Vec<PathBuf> {
    match std::fs::symlink_metadata(folder) {
        Ok(meta) if meta.is_dir() && !is_reparse_point(&meta) => {}
        Ok(_) => {
            eprintln!("tack: {} is not a plain folder; nothing in it is swept", folder.display());
            return Vec::new();
        }
        Err(_) => return Vec::new(),
    }
    let Ok(home) = std::fs::canonicalize(folder) else { return Vec::new() };
    let kept = kept_names(&home, keep);
    let Ok(entries) = std::fs::read_dir(folder) else { return Vec::new() };
    entries
        .flatten()
        .map(|e| e.path())
        .filter(|path| {
            let Ok(meta) = std::fs::symlink_metadata(path) else { return false };
            meta.is_file()
                && !is_reparse_point(&meta)
                && (ours(path) || is_partial(path))
                && !kept.contains(&name_key(path))
                && meta.modified().is_ok_and(|modified| modified < cutoff)
        })
        .filter(|path| recycle_file(path, bin))
        .collect()
}

/// The names (any case) of the files in the folder `home` (resolved) that
/// `keep` names. A path counts by its folder, resolved, so another spelling
/// of the same folder (an 8.3 short name, `\\?\`, a trailing separator)
/// cannot make a pinned file look unreferenced; when its folder cannot be
/// resolved at all, its name is kept anyway, to be safe.
fn kept_names(home: &Path, keep: &[PathBuf]) -> HashSet<String> {
    let home = path_key(home);
    keep.iter()
        .filter_map(|path| {
            // The file itself first, which also turns a short name long.
            let resolved = std::fs::canonicalize(path).ok();
            let path = resolved.as_deref().unwrap_or(path);
            let name = path.file_name()?;
            let here = match path.parent().map(std::fs::canonicalize) {
                Some(Ok(dir)) => path_key(&dir) == home,
                _ => true,
            };
            here.then(|| name.to_string_lossy().to_lowercase())
        })
        .collect()
}

fn name_key(path: &Path) -> String {
    path.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default()
}

/// A link, a junction, a cloud placeholder or the like: something that may
/// stand for a file somewhere else.
#[cfg(windows)]
fn is_reparse_point(meta: &Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;
    meta.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0
}

#[cfg(not(windows))]
fn is_reparse_point(meta: &Metadata) -> bool {
    meta.file_type().is_symlink()
}

/// Recycles a capture's file once its print has left the board. Only files
/// in Tack's own `folder` are touched; a screenshot file never is.
pub fn discard_file(print: &Print, folder: &Path, bin: &dyn Recycler) {
    if print.owned() {
        discard_path(&print.path, folder, bin);
    }
}

/// Recycles a file Tack wrote (a capture or a note) that it no longer needs,
/// if it lies in one of Tack's own folders; anything else is left alone.
pub fn discard_owned(path: &Path, bin: &dyn Recycler) {
    for folder in [captures_folder(), crate::note::notes_folder()] {
        discard_path(path, &folder, bin);
    }
}

/// Recycles a file Tack no longer needs, if it is a file directly in
/// `folder`, named plainly (no `..` that could lead out of it).
fn discard_path(path: &Path, folder: &Path, bin: &dyn Recycler) {
    if is_plain(path) && path.parent().is_some_and(|dir| path_key(dir) == path_key(folder)) && path.is_file() {
        recycle_file(path, bin);
    }
}

/// Tells a fresh capture from the same pixels arriving again moments later.
#[derive(Debug, Default)]
pub struct RepeatFilter {
    /// The previous capture's pixel hash and when it was read.
    last: Option<(u64, Instant)>,
}

impl RepeatFilter {
    pub fn new() -> RepeatFilter {
        RepeatFilter::default()
    }

    /// Records a capture with this [`crate::thumbnail::pixel_hash`], read at
    /// `now`, and says whether it repeats the previous capture.
    pub fn is_repeat(&mut self, hash: u64, now: Instant) -> bool {
        let repeat = self.last.is_some_and(|(h, at)| h == hash && now.saturating_duration_since(at) < REPEAT_WINDOW);
        self.last = Some((hash, now));
        repeat
    }
}

/// Screenshots have no transparency. Dropping an all-opaque alpha channel
/// makes a smaller PNG, and an all-zero one (a bitmap that never set it)
/// would otherwise save an invisible picture.
pub fn without_alpha(rgba: RgbaImage) -> DynamicImage {
    let flat = rgba.pixels().all(|p| p[3] == 255) || rgba.pixels().all(|p| p[3] == 0);
    let img = DynamicImage::ImageRgba8(rgba);
    if flat {
        DynamicImage::ImageRgb8(img.to_rgb8())
    } else {
        img
    }
}

/// Local wall-clock time, for naming a capture.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Timestamp {
    pub year: u16,
    pub month: u16,
    pub day: u16,
    pub hour: u16,
    pub minute: u16,
    pub second: u16,
}

impl Timestamp {
    /// `Screenshot YYYY-MM-DD HHMMSS`, the name Snipping Tool itself would
    /// give the file.
    pub fn file_stem(&self) -> String {
        format!("Screenshot {}", self.stamp())
    }

    /// `YYYY-MM-DD HHMMSS`.
    pub fn stamp(&self) -> String {
        format!(
            "{:04}-{:02}-{:02} {:02}{:02}{:02}",
            self.year, self.month, self.day, self.hour, self.minute, self.second
        )
    }
}

/// Appended to a capture's file name while Tack is still writing it. Not an
/// image extension, so the folder watcher and the board never mistake a
/// half-written capture for a picture.
const PARTIAL: &str = ".part";

/// Where a capture is written before it is moved to `path`.
pub fn partial_path(path: &Path) -> PathBuf {
    let mut name = path.as_os_str().to_owned();
    name.push(PARTIAL);
    PathBuf::from(name)
}

fn is_partial(path: &Path) -> bool {
    path.file_name().and_then(|n| n.to_str()).is_some_and(|n| n.ends_with(PARTIAL))
}

/// Picks the name a capture will have, `Screenshot YYYY-MM-DD HHMMSS.png` in
/// `dir` (adding " (2)" and so on if that name is taken), and claims it by
/// creating its empty partial file. Fast, so a capture can be pinned before
/// its picture is written: [`write_reserved`] does that, and only then does
/// the file exist under its name.
pub fn reserve(dir: &Path, at: Timestamp) -> std::io::Result<PathBuf> {
    reserve_as(dir, &at.file_stem(), "png")
}

/// [`reserve`] for any name: `<stem>.<ext>` in `dir`, or `<stem> (2).<ext>`
/// and so on if that is taken.
pub(crate) fn reserve_as(dir: &Path, stem: &str, ext: &str) -> std::io::Result<PathBuf> {
    std::fs::create_dir_all(dir)?;
    for n in 1.. {
        let name = if n == 1 { format!("{stem}.{ext}") } else { format!("{stem} ({n}).{ext}") };
        let path = dir.join(name);
        if path.exists() {
            continue;
        }
        match OpenOptions::new().write(true).create_new(true).open(partial_path(&path)) {
            Ok(_) => return Ok(path),
            Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => continue,
            Err(e) => return Err(e),
        }
    }
    unreachable!()
}

/// Writes the capture as a PNG into the partial file [`reserve`] made for
/// `path`, then moves it into place. On failure the partial file is removed.
pub fn write_reserved(img: &DynamicImage, path: &Path) -> std::io::Result<()> {
    let mut png = Vec::new();
    img.write_to(&mut Cursor::new(&mut png), ImageFormat::Png).map_err(std::io::Error::other)?;
    write_reserved_bytes(&png, path)
}

/// Writes `bytes` into the partial file [`reserve_as`] made for `path`, then
/// moves it into place. On failure the partial file is removed.
pub(crate) fn write_reserved_bytes(bytes: &[u8], path: &Path) -> std::io::Result<()> {
    let partial = partial_path(path);
    let written = (|| {
        let mut file = OpenOptions::new().write(true).truncate(true).open(&partial)?;
        file.write_all(bytes)?;
        // On disk before its name says it is whole: a crash just after the
        // rename must not leave an empty or half-written picture.
        file.sync_all()?;
        drop(file);
        std::fs::rename(&partial, path)
    })();
    if written.is_err() {
        let _ = std::fs::remove_file(&partial);
    }
    written
}

/// Saves a picture's own bytes (a PNG or JPEG dropped on the board) as
/// `<stem> YYYY-MM-DD HHMMSS.<ext>` in `dir`, unchanged.
pub fn save_bytes(bytes: &[u8], dir: &Path, stem: &str, ext: &str, at: Timestamp) -> std::io::Result<PathBuf> {
    let path = reserve_as(dir, &format!("{stem} {}", at.stamp()), ext)?;
    write_reserved_bytes(bytes, &path)?;
    Ok(path)
}

/// Saves as `Screenshot YYYY-MM-DD HHMMSS.png` in `dir`, adding " (2)" and so
/// on if that name is taken: [`reserve`] and [`write_reserved`] in one go.
pub fn save(img: &DynamicImage, dir: &Path, at: Timestamp) -> std::io::Result<PathBuf> {
    let path = reserve(dir, at)?;
    write_reserved(img, &path)?;
    Ok(path)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::print::{Kind, Origin};
    use crate::recycle::testing::NotingBin;
    use crate::thumbnail::pixel_hash;
    use image::Rgba;

    fn image(fill: u8) -> DynamicImage {
        DynamicImage::ImageRgba8(RgbaImage::from_pixel(4, 3, Rgba([fill, fill, fill, 255])))
    }

    /// A fresh, empty folder for one test.
    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("tack-capture-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// Nine days from now: every file a test just wrote is past the grace.
    fn nine_days_on() -> SystemTime {
        sweep_cutoff(SystemTime::now() + Duration::from_secs(9 * 24 * 60 * 60), None)
    }

    #[test]
    fn the_same_pixels_soon_after_are_a_repeat() {
        let mut filter = RepeatFilter::new();
        let now = Instant::now();
        let hash = pixel_hash(&image(10));
        assert!(!filter.is_repeat(hash, now));
        assert!(filter.is_repeat(hash, now + Duration::from_secs(1)));
        // Each read restarts the window.
        assert!(filter.is_repeat(hash, now + Duration::from_secs(5)));
    }

    #[test]
    fn other_pixels_or_a_later_copy_are_new() {
        let mut filter = RepeatFilter::new();
        let now = Instant::now();
        assert!(!filter.is_repeat(pixel_hash(&image(10)), now));
        assert!(!filter.is_repeat(pixel_hash(&image(20)), now));
        assert!(!filter.is_repeat(pixel_hash(&image(20)), now + REPEAT_WINDOW));
    }

    #[test]
    fn flat_alpha_is_dropped() {
        let opaque = RgbaImage::from_pixel(2, 2, Rgba([1, 2, 3, 255]));
        assert!(!without_alpha(opaque).color().has_alpha());
        let unset = RgbaImage::from_pixel(2, 2, Rgba([1, 2, 3, 0]));
        assert!(!without_alpha(unset).color().has_alpha());
        let mut mixed = RgbaImage::from_pixel(2, 2, Rgba([1, 2, 3, 255]));
        mixed.put_pixel(0, 0, Rgba([1, 2, 3, 128]));
        assert!(without_alpha(mixed).color().has_alpha());
    }

    #[test]
    fn captures_are_named_like_snipping_tool_files() {
        let at = Timestamp { year: 2026, month: 10, day: 7, hour: 9, minute: 5, second: 3 };
        assert_eq!(at.file_stem(), "Screenshot 2026-10-07 090503");
    }

    #[test]
    fn saving_twice_in_a_second_picks_a_free_name() {
        let dir = scratch("save");
        let at = Timestamp { year: 2026, month: 1, day: 2, hour: 3, minute: 4, second: 5 };
        let first = save(&image(1), &dir, at).unwrap();
        let second = save(&image(2), &dir, at).unwrap();
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(first.file_name().unwrap(), "Screenshot 2026-01-02 030405.png");
        assert_eq!(second.file_name().unwrap(), "Screenshot 2026-01-02 030405 (2).png");
    }

    #[test]
    fn a_reserved_name_is_taken_until_written_and_then_moved_into_place() {
        let dir = scratch("reserve");
        let at = Timestamp { year: 2026, month: 1, day: 2, hour: 3, minute: 4, second: 5 };
        let first = reserve(&dir, at).unwrap();
        let second = reserve(&dir, at).unwrap();
        let (first_exists, partial_exists) = (first.exists(), partial_path(&first).exists());
        write_reserved(&image(1), &first).unwrap();
        let written = first.is_file() && !partial_path(&first).exists();
        let third = reserve(&dir, at).unwrap();
        let _ = std::fs::remove_dir_all(&dir);
        assert!(!first_exists && partial_exists, "only the partial file exists before the write");
        assert_eq!(second.file_name().unwrap(), "Screenshot 2026-01-02 030405 (2).png");
        assert!(written);
        assert_eq!(third.file_name().unwrap(), "Screenshot 2026-01-02 030405 (3).png");
    }

    #[test]
    fn the_sweep_takes_only_old_unpinned_images() {
        let dir = scratch("sweep");
        let file = |name: &str| {
            let path = dir.join(name);
            std::fs::write(&path, b"x").unwrap();
            path
        };
        let pinned = file("Pinned.png");
        let leftover = file("Leftover.png");
        let partial = file("Crashed.png.part");
        // Not an image, so not a capture Tack wrote.
        file("notes.txt");
        // Pinned under another case: Windows paths ignore it.
        let pinned_upper = PathBuf::from(pinned.to_string_lossy().to_uppercase());

        let bin = NotingBin::default();
        let swept = sweep(&dir, std::slice::from_ref(&pinned_upper), nine_days_on(), &bin);
        // A day from now, the same leftover is far too young to touch.
        let young = NotingBin::default();
        let tomorrow = SystemTime::now() + Duration::from_secs(24 * 60 * 60);
        let swept_soon = sweep(&dir, &[pinned_upper], sweep_cutoff(tomorrow, None), &young);
        let _ = std::fs::remove_dir_all(&dir);

        let mut swept = swept;
        swept.sort();
        assert_eq!(swept, vec![partial.clone(), leftover.clone()]);
        assert_eq!(bin.taken.borrow().len(), 2);
        assert!(swept_soon.is_empty(), "a capture saved in the last eight days is never swept");
        assert!(young.taken.borrow().is_empty());
    }

    #[test]
    fn nothing_written_after_board_json_was_last_saved_is_swept() {
        let dir = scratch("sweep-after-save");
        let saved = SystemTime::now() - Duration::from_secs(60);
        let leftover = dir.join("Unlisted.png");
        std::fs::write(&leftover, b"x").unwrap();
        let later = SystemTime::now() + Duration::from_secs(9 * 24 * 60 * 60);
        let bin = NotingBin::default();
        let swept = sweep(&dir, &[], sweep_cutoff(later, Some(saved)), &bin);
        let _ = std::fs::remove_dir_all(&dir);
        assert!(swept.is_empty(), "board.json may simply have failed to save since");
        assert_eq!(sweep_cutoff(later, Some(later)), later - SWEEP_GRACE);
    }

    #[test]
    fn a_pinned_file_counts_however_its_folder_is_spelled() {
        let dir = scratch("sweep-spelling");
        let pinned = dir.join("Screenshot 2026-10-08 023223.png");
        std::fs::write(&pinned, b"x").unwrap();
        let elsewhere = dir.join("Elsewhere");
        std::fs::create_dir_all(&elsewhere).unwrap();
        let trailing = PathBuf::from(format!("{}{}", dir.display(), std::path::MAIN_SEPARATOR));
        let spellings = [
            // The resolved form, `\\?\C:\...` on Windows.
            std::fs::canonicalize(&pinned).unwrap(),
            trailing.join("Screenshot 2026-10-08 023223.png"),
            dir.join("Elsewhere").join("..").join("SCREENSHOT 2026-10-08 023223.PNG"),
            // A folder that cannot be found: the name alone is kept, to be safe.
            PathBuf::from(r"Q:\gone\Screenshot 2026-10-08 023223.png"),
        ];
        let mut taken = Vec::new();
        for spelling in &spellings {
            let bin = NotingBin::default();
            sweep(&dir, std::slice::from_ref(spelling), nine_days_on(), &bin);
            taken.push(bin.taken.borrow().len());
        }
        // The same name in another folder that exists does not count.
        let bin = NotingBin::default();
        sweep(&dir, &[elsewhere.join("Screenshot 2026-10-08 023223.png")], nine_days_on(), &bin);
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(taken, [0, 0, 0, 0]);
        assert_eq!(*bin.taken.borrow(), vec![pinned]);
    }

    #[cfg(windows)]
    #[test]
    fn links_are_never_swept_and_a_linked_folder_not_at_all() {
        let dir = scratch("sweep-links");
        let real = dir.join("Real");
        std::fs::create_dir_all(&real).unwrap();
        std::fs::write(real.join("a.png"), b"x").unwrap();
        // Creating links needs Developer Mode or elevation; without it there
        // is nothing to test.
        let file_link = std::os::windows::fs::symlink_file(real.join("a.png"), dir.join("link.png")).is_ok();
        let dir_link = std::os::windows::fs::symlink_dir(&real, dir.join("Linked")).is_ok();
        let bin = NotingBin::default();
        sweep(&dir, &[], nine_days_on(), &bin);
        let through = NotingBin::default();
        sweep(&dir.join("Linked"), &[], nine_days_on(), &through);
        let _ = std::fs::remove_dir_all(&dir);
        if file_link {
            assert!(bin.taken.borrow().is_empty(), "a link is not a capture");
        }
        if dir_link {
            assert!(through.taken.borrow().is_empty(), "a linked folder is never swept");
        }
    }

    #[test]
    fn discarding_only_touches_captures_in_tacks_own_folder() {
        let root = scratch("discard");
        let folder = root.join("Captures");
        std::fs::create_dir_all(&folder).unwrap();
        let file = |path: PathBuf| {
            std::fs::write(&path, b"x").unwrap();
            path
        };
        let ours = file(folder.join("ours.png"));
        let moved = file(folder.join("saved to pictures.png"));
        let elsewhere = file(root.join("elsewhere.png"));
        std::fs::create_dir_all(folder.join("folder.png")).unwrap();
        let print = |path: &Path, origin| Print {
            id: "1".into(),
            name: String::new(),
            kind: Kind::Image,
            thumb: String::new(),
            width: 1,
            height: 1,
            hash: None,
            pinned_at: 0,
            kept: false,
            kept_at: None,
            note: None,
            path: path.to_path_buf(),
            stamp: None,
            origin,
            arrived: None,
        };
        let bin = NotingBin::default();
        discard_file(&print(&ours, Origin::Capture), &folder, &bin);
        discard_file(&print(&moved, Origin::Folder), &folder, &bin);
        discard_file(&print(&elsewhere, Origin::Capture), &folder, &bin);
        // Not a file, and not plainly in the folder.
        discard_file(&print(&folder.join("folder.png"), Origin::Capture), &folder, &bin);
        discard_file(&print(&folder.join("..").join("elsewhere.png"), Origin::Capture), &folder, &bin);
        let _ = std::fs::remove_dir_all(&root);
        assert_eq!(*bin.taken.borrow(), vec![ours]);
    }
}
