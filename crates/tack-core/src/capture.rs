//! Captures: Snipping Tool images that only ever existed on the clipboard.
//! Tack saves each one as a PNG in its own folder so it can be dragged,
//! opened and edited like any screenshot file. When the print leaves the
//! board the file goes to the Recycle Bin (never deleted outright), since
//! the user never chose to keep that file but may still want it back.

use std::collections::HashSet;
use std::fs::OpenOptions;
use std::hash::{DefaultHasher, Hash, Hasher};
use std::io::{Cursor, Write};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant, SystemTime};

use image::{DynamicImage, ImageFormat, RgbaImage};

use crate::files::{is_image, path_key};
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

/// A capture this young is never swept, pinned or not: it may have been
/// saved just before a crash, before board.json could mention it.
pub(crate) const SWEEP_GRACE: Duration = Duration::from_secs(10 * 60);

/// Recycles the leftover captures in `folder`: images no print in `pinned`
/// refers to (for example after a crash) and half-written ones, but nothing
/// modified within [`SWEEP_GRACE`] of `now`. Returns the files that went.
///
/// Only call this when board.json was read and fully understood: if Tack
/// cannot say for sure which captures are pinned, every one of them would
/// look like a leftover.
pub fn sweep(folder: &Path, pinned: &[PathBuf], now: SystemTime, bin: &dyn Recycler) -> Vec<PathBuf> {
    sweep_where(folder, pinned, now, bin, is_image)
}

/// [`sweep`] for any folder of Tack's own files: `ours` says which files in
/// it Tack wrote (half-written ones always count).
pub(crate) fn sweep_where(
    folder: &Path,
    pinned: &[PathBuf],
    now: SystemTime,
    bin: &dyn Recycler,
    ours: impl Fn(&Path) -> bool,
) -> Vec<PathBuf> {
    let keep: HashSet<String> = pinned.iter().map(|p| path_key(p)).collect();
    let Ok(entries) = std::fs::read_dir(folder) else { return Vec::new() };
    let old_enough = |path: &Path| {
        std::fs::metadata(path)
            .and_then(|m| m.modified())
            .is_ok_and(|modified| now.duration_since(modified).is_ok_and(|age| age >= SWEEP_GRACE))
    };
    entries
        .flatten()
        .map(|e| e.path())
        .filter(|path| {
            path.is_file() && (ours(path) || is_partial(path)) && !keep.contains(&path_key(path)) && old_enough(path)
        })
        .filter(|path| recycle_file(path, bin))
        .collect()
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

/// Recycles a file Tack no longer needs, if it lies in `folder`.
fn discard_path(path: &Path, folder: &Path, bin: &dyn Recycler) {
    if path.parent().is_some_and(|dir| path_key(dir) == path_key(folder)) {
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

    /// Records `img` as read at `now`, and says whether it repeats the
    /// previous capture.
    pub fn is_repeat(&mut self, img: &DynamicImage, now: Instant) -> bool {
        let mut hasher = DefaultHasher::new();
        (img.width(), img.height(), img.as_bytes()).hash(&mut hasher);
        let hash = hasher.finish();
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
        file.flush()?;
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
    use image::Rgba;

    fn image(fill: u8) -> DynamicImage {
        DynamicImage::ImageRgba8(RgbaImage::from_pixel(4, 3, Rgba([fill, fill, fill, 255])))
    }

    #[test]
    fn the_same_pixels_soon_after_are_a_repeat() {
        let mut filter = RepeatFilter::new();
        let now = Instant::now();
        assert!(!filter.is_repeat(&image(10), now));
        assert!(filter.is_repeat(&image(10), now + Duration::from_secs(1)));
        // Each read restarts the window.
        assert!(filter.is_repeat(&image(10), now + Duration::from_secs(5)));
    }

    #[test]
    fn other_pixels_or_a_later_copy_are_new() {
        let mut filter = RepeatFilter::new();
        let now = Instant::now();
        assert!(!filter.is_repeat(&image(10), now));
        assert!(!filter.is_repeat(&image(20), now));
        assert!(!filter.is_repeat(&image(20), now + REPEAT_WINDOW));
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
        let dir = std::env::temp_dir().join(format!("tack-capture-test-{}", std::process::id()));
        let at = Timestamp { year: 2026, month: 1, day: 2, hour: 3, minute: 4, second: 5 };
        let first = save(&image(1), &dir, at).unwrap();
        let second = save(&image(2), &dir, at).unwrap();
        let _ = std::fs::remove_dir_all(&dir);
        assert_eq!(first.file_name().unwrap(), "Screenshot 2026-01-02 030405.png");
        assert_eq!(second.file_name().unwrap(), "Screenshot 2026-01-02 030405 (2).png");
    }

    #[test]
    fn a_reserved_name_is_taken_until_written_and_then_moved_into_place() {
        let dir = std::env::temp_dir().join(format!("tack-reserve-test-{}", std::process::id()));
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
        let dir = std::env::temp_dir().join(format!("tack-sweep-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
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
        // Judged an hour from now, every file above is past the grace period.
        let later = SystemTime::now() + Duration::from_secs(60 * 60);
        // Pinned under another case: Windows paths ignore it.
        let pinned_upper = PathBuf::from(pinned.to_string_lossy().to_uppercase());

        let bin = NotingBin::default();
        let swept = sweep(&dir, std::slice::from_ref(&pinned_upper), later, &bin);
        // Judged now, the same leftover is too young to touch.
        let young = NotingBin::default();
        let swept_now = sweep(&dir, &[pinned_upper], SystemTime::now(), &young);
        let _ = std::fs::remove_dir_all(&dir);

        let mut swept = swept;
        swept.sort();
        assert_eq!(swept, vec![partial.clone(), leftover.clone()]);
        assert_eq!(bin.taken.borrow().len(), 2);
        assert!(swept_now.is_empty(), "a capture saved in the last ten minutes is never swept");
        assert!(young.taken.borrow().is_empty());
    }

    #[test]
    fn discarding_only_touches_captures_in_tacks_own_folder() {
        let root = std::env::temp_dir().join(format!("tack-discard-test-{}", std::process::id()));
        let folder = root.join("Captures");
        std::fs::create_dir_all(&folder).unwrap();
        let file = |path: PathBuf| {
            std::fs::write(&path, b"x").unwrap();
            path
        };
        let ours = file(folder.join("ours.png"));
        let moved = file(folder.join("saved to pictures.png"));
        let elsewhere = file(root.join("elsewhere.png"));
        let print = |path: &Path, origin| Print {
            id: "1".into(),
            name: String::new(),
            kind: Kind::Image,
            thumb: String::new(),
            width: 1,
            height: 1,
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
        let _ = std::fs::remove_dir_all(&root);
        assert_eq!(*bin.taken.borrow(), vec![ours]);
    }
}
