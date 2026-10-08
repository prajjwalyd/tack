//! Decoding screenshots: the small JPEG prints for the board, the full image
//! for the clipboard, the little bitmap that follows the pointer in a drag,
//! and the pixel hash that tells one picture from another of the same size.

use std::hash::{DefaultHasher, Hash, Hasher};
use std::io::{BufRead, Cursor, Seek};
use std::path::Path;
use std::sync::{Condvar, Mutex};
use std::time::{Duration, Instant};

use base64::Engine;
use image::codecs::jpeg::JpegEncoder;
use image::{DynamicImage, GenericImageView, ImageReader, Limits, RgbImage};

/// Long side of a print on the board, in pixels.
const THUMB_SIDE: u32 = 360;
const THUMB_QUALITY: u8 = 85;
/// Long side of the drag image.
const DRAG_SIDE: u32 = 160;
/// Long side of the picture a new capture flies onto the board with: about
/// the snip itself on most screens, so it looks like the snip lifting off.
const FLIGHT_SIDE: u32 = 1600;
const FLIGHT_QUALITY: u8 = 80;

/// A print's picture, as the UI gets it.
#[derive(Clone, Debug)]
pub struct Thumb {
    /// `data:image/jpeg;base64,...`, long side at most 360 px.
    pub data_url: String,
    /// The original image's size.
    pub width: u32,
    pub height: u32,
    /// The original image's [`pixel_hash`], when it was taken from the
    /// full picture. Two prints are only ever one picture if theirs match.
    pub hash: Option<u64>,
}

/// The most any one picture may claim: 16384 px a side and 256 MiB of
/// pixels. A file that claims more (a tiny PNG can claim gigabytes) is
/// refused instead of exhausting memory.
const MAX_SIDE: u32 = 16_384;
const MAX_ALLOC: u64 = 256 * 1024 * 1024;
/// Decodes at once, however many pictures arrive together.
const MAX_DECODES: usize = 2;

static DECODES: (Mutex<usize>, Condvar) = (Mutex::new(0), Condvar::new());

/// A turn to decode; the next waiting decode starts when it is dropped.
struct Turn;

impl Turn {
    fn take() -> Turn {
        let (count, freed) = &DECODES;
        let mut n = count.lock().unwrap_or_else(|p| p.into_inner());
        while *n >= MAX_DECODES {
            n = freed.wait(n).unwrap_or_else(|p| p.into_inner());
        }
        *n += 1;
        Turn
    }
}

impl Drop for Turn {
    fn drop(&mut self) {
        let (count, freed) = &DECODES;
        *count.lock().unwrap_or_else(|p| p.into_inner()) -= 1;
        freed.notify_one();
    }
}

fn limited<R: BufRead + Seek>(reader: ImageReader<R>) -> Result<DynamicImage, String> {
    let mut reader = reader.with_guessed_format().map_err(|e| e.to_string())?;
    let mut limits = Limits::default();
    limits.max_image_width = Some(MAX_SIDE);
    limits.max_image_height = Some(MAX_SIDE);
    limits.max_alloc = Some(MAX_ALLOC);
    reader.limits(limits);
    let _turn = Turn::take();
    reader.decode().map_err(|e| e.to_string())
}

/// Decodes a picture file, within the limits above.
pub fn decode(path: &Path) -> Result<DynamicImage, String> {
    limited(ImageReader::open(path).map_err(|e| e.to_string())?)
}

/// Decodes a picture already in memory (a drop, a phone pin), within the
/// same limits.
pub fn decode_bytes(bytes: &[u8]) -> Result<DynamicImage, String> {
    limited(ImageReader::new(Cursor::new(bytes)))
}

pub fn make(path: &Path) -> Result<Thumb, String> {
    from_image(decode(path)?)
}

/// For an image already in memory, such as a clipboard capture.
pub fn from_image(img: DynamicImage) -> Result<Thumb, String> {
    let hash = pixel_hash(&img);
    shrunk(img, Some(hash))
}

/// The thumbnail of `img`, which also gives the size; `hash` is the full
/// picture's.
fn shrunk(img: DynamicImage, hash: Option<u64>) -> Result<Thumb, String> {
    let (width, height) = img.dimensions();
    let small = if width.max(height) > THUMB_SIDE { img.thumbnail(THUMB_SIDE, THUMB_SIDE) } else { img };
    Ok(Thumb { data_url: jpeg_data_url(&small, THUMB_QUALITY)?, width, height, hash })
}

/// For a capture that flies onto the board: the picture it flies with (a
/// JPEG data URL, long side at most 1600 px) and its thumbnail, drawn from
/// that picture rather than from the full image, which is quicker. `hash`
/// is the full image's [`pixel_hash`], already taken to spot a repeat.
pub fn for_flight(img: &DynamicImage, hash: u64) -> Result<(String, Thumb), String> {
    let (width, height) = img.dimensions();
    let flight = if width.max(height) > FLIGHT_SIDE { img.thumbnail(FLIGHT_SIDE, FLIGHT_SIDE) } else { img.clone() };
    let flight_url = jpeg_data_url(&flight, FLIGHT_QUALITY)?;
    let thumb = shrunk(flight, Some(hash))?;
    Ok((flight_url, Thumb { width, height, ..thumb }))
}

/// A hash of the picture's size and its pixels' colours, alpha left out, so
/// the same picture hashes alike off the clipboard (often RGBA) and out of
/// a PNG (often RGB). Fed row by row the same way for every layout, since
/// a hasher need not give the same result for differently split input.
pub fn pixel_hash(img: &DynamicImage) -> u64 {
    let mut hasher = DefaultHasher::new();
    let (width, height) = img.dimensions();
    (width, height).hash(&mut hasher);
    if width == 0 || height == 0 {
        return hasher.finish();
    }
    let row_len = width as usize * 3;
    match img {
        DynamicImage::ImageRgb8(rgb) => rgb.as_raw().chunks_exact(row_len).for_each(|row| hasher.write(row)),
        DynamicImage::ImageRgba8(rgba) => {
            let mut row = Vec::with_capacity(row_len);
            for src in rgba.as_raw().chunks_exact(width as usize * 4) {
                row.clear();
                src.as_chunks::<4>().0.iter().for_each(|px| row.extend_from_slice(&px[..3]));
                hasher.write(&row);
            }
        }
        other => other.to_rgb8().as_raw().chunks_exact(row_len).for_each(|row| hasher.write(row)),
    }
    hasher.finish()
}

fn jpeg_data_url(img: &DynamicImage, quality: u8) -> Result<String, String> {
    let rgb = flatten(img);
    let mut jpeg = Vec::new();
    JpegEncoder::new_with_quality(&mut jpeg, quality).encode_image(&rgb).map_err(|e| e.to_string())?;
    Ok(format!("data:image/jpeg;base64,{}", base64::engine::general_purpose::STANDARD.encode(&jpeg)))
}

/// Snipping Tool creates the file first and writes it a moment later, so a
/// fresh screenshot is retried for a while before giving up on it.
pub fn make_patiently(path: &Path, patience: Duration) -> Result<Thumb, String> {
    let start = Instant::now();
    loop {
        match make(path) {
            Ok(thumb) => return Ok(thumb),
            Err(e) if start.elapsed() >= patience || !path.exists() => return Err(e),
            Err(_) => std::thread::sleep(Duration::from_millis(150)),
        }
    }
}

/// JPEG has no alpha: transparent parts go on white paper, not black.
fn flatten(img: &DynamicImage) -> RgbImage {
    if !img.color().has_alpha() {
        return img.to_rgb8();
    }
    let rgba = img.to_rgba8();
    let (w, h) = rgba.dimensions();
    let mut out = RgbImage::new(w, h);
    for (x, y, p) in rgba.enumerate_pixels() {
        let a = p[3] as u32;
        let blend = |c: u8| ((c as u32 * a + 255 * (255 - a)) / 255) as u8;
        out.put_pixel(x, y, image::Rgb([blend(p[0]), blend(p[1]), blend(p[2])]));
    }
    out
}

/// The drag image: premultiplied BGRA, top-down rows, long side ~160 px.
pub struct DragImage {
    pub width: i32,
    pub height: i32,
    pub bgra: Vec<u8>,
}

/// Built from the print already on the board, so it costs nothing to decode.
pub fn drag_image(data_url: &str) -> Option<DragImage> {
    let b64 = data_url.split_once(',')?.1;
    let bytes = base64::engine::general_purpose::STANDARD.decode(b64).ok()?;
    let img = ImageReader::new(Cursor::new(bytes)).with_guessed_format().ok()?.decode().ok()?;
    let small = img.resize(DRAG_SIDE, DRAG_SIDE, image::imageops::FilterType::Triangle);
    let rgba = small.to_rgba8();
    let (w, h) = rgba.dimensions();
    let mut bgra = Vec::with_capacity((w * h * 4) as usize);
    for p in rgba.pixels() {
        let a = p[3] as u32;
        let pm = |c: u8| ((c as u32 * a) / 255) as u8;
        bgra.extend_from_slice(&[pm(p[2]), pm(p[1]), pm(p[0]), p[3]]);
    }
    Some(DragImage { width: w as i32, height: h as i32, bgra })
}

#[cfg(test)]
mod tests {
    use super::*;
    use image::{Rgba, RgbaImage};

    #[test]
    fn thumbnails_keep_the_original_size_and_shrink_the_picture() {
        let img = DynamicImage::ImageRgba8(RgbaImage::from_pixel(1200, 600, Rgba([10, 20, 30, 255])));
        let thumb = from_image(img).unwrap();
        assert_eq!((thumb.width, thumb.height), (1200, 600));
        let drag = drag_image(&thumb.data_url).unwrap();
        assert_eq!((drag.width, drag.height), (160, 80));
        assert_eq!(drag.bgra.len(), 160 * 80 * 4);
    }

    #[test]
    fn a_flight_picture_is_capped_and_its_thumbnail_keeps_the_original_size() {
        let img = DynamicImage::ImageRgb8(RgbImage::from_pixel(3200, 1600, image::Rgb([200, 100, 50])));
        let (flight, thumb) = for_flight(&img, pixel_hash(&img)).unwrap();
        assert!(flight.starts_with("data:image/jpeg;base64,"));
        assert_eq!((thumb.width, thumb.height), (3200, 1600));
        assert_eq!(thumb.hash, from_image(img).unwrap().hash, "the full picture's hash, not the flight's");
        let drag = drag_image(&flight).unwrap();
        assert_eq!((drag.width, drag.height), (160, 80));
    }

    #[test]
    fn the_same_pixels_hash_alike_in_any_layout_and_others_do_not() {
        let rgba = RgbaImage::from_fn(5, 3, |x, y| Rgba([x as u8, y as u8, 7, 255]));
        let rgb = DynamicImage::ImageRgba8(rgba.clone()).to_rgb8();
        let hash = pixel_hash(&DynamicImage::ImageRgba8(rgba.clone()));
        assert_eq!(hash, pixel_hash(&DynamicImage::ImageRgb8(rgb.clone())));
        assert_eq!(hash, pixel_hash(&DynamicImage::ImageRgb16(DynamicImage::ImageRgb8(rgb).to_rgb16())));
        let mut other = rgba.clone();
        other.put_pixel(4, 2, Rgba([4, 2, 8, 255]));
        assert_ne!(hash, pixel_hash(&DynamicImage::ImageRgba8(other)));
        // The same bytes in another shape are another picture.
        let reshaped = RgbaImage::from_raw(3, 5, rgba.into_raw()).unwrap();
        assert_ne!(hash, pixel_hash(&DynamicImage::ImageRgba8(reshaped)));
        assert_eq!(pixel_hash(&DynamicImage::new_rgb8(0, 0)), pixel_hash(&DynamicImage::new_rgba8(0, 0)));
    }

    #[test]
    fn transparency_goes_on_white_paper() {
        let img = DynamicImage::ImageRgba8(RgbaImage::from_pixel(1, 1, Rgba([0, 0, 0, 0])));
        assert_eq!(flatten(&img).get_pixel(0, 0).0, [255, 255, 255]);
    }
}
