//! Where on screen a snip was taken, worked out after the fact.
//!
//! Snipping Tool puts the picture on the clipboard the moment the drag that
//! selected it ends, so when the clipboard update arrives the pointer is
//! normally still on one corner of the selection, and the picture's size is
//! the selection's size (both in physical pixels). That leaves four
//! rectangles the picture can have come from, one for each corner the
//! pointer may be on. A window snip or a full-screen snip adds the window
//! under the pointer or the whole monitor, when their size matches.
//!
//! Which one it was is decided by comparing the picture with the screen.
//! Both are point-sampled on the same fixed grid of positions, relative to
//! the picture, so a candidate in exactly the right place reads exactly the
//! same pixels as the picture and scores 1.0, while a wrong one scores far
//! lower. If no exact candidate is convincing (the pointer drifted a few
//! pixels before the clipboard update came), the corners are nudged around
//! within [`RADIUS`].
//!
//! Pure maths, no Win32: `tack_windows::capture_origin` grabs the screen and
//! calls in here, so all of this is tested with made-up screens.

use crate::view::Rect;

/// Samples per side of the comparison grid.
pub const GRID: u32 = 32;
/// An exact candidate this good ends the search at once.
pub const SURE: f64 = 0.97;
/// The least a candidate must score to be believed at all.
pub const ACCEPT: f64 = 0.86;
/// How far, in physical px, a corner is nudged looking for the picture when
/// the pointer has moved since the drag ended.
pub const RADIUS: i32 = 12;
/// Half the side of the square around the pointer that a quick look
/// compares (see [`focus`]). Reading the screen costs about 15 ms however
/// little is read, and more the more is read; a patch this size next to the
/// pointer is plenty to tell the candidates apart.
pub const FOCUS: i32 = 256;
/// Below this [`Samples::detail`] a patch is too plain to tell candidates
/// apart: compare the whole picture instead.
pub const PLAIN: f64 = 6.0;
/// Scores this close to the best count as a tie, settled by which
/// candidate is likelier ([`Corner::ALL`]'s order, then window, monitor).
const TIE: f64 = 0.01;
/// A sample's colour difference (summed over R, G and B) at which it counts
/// as wholly different. Small differences (a caret, a clock that ticked,
/// compression on either side) cost little.
const CAP: u32 = 120;

/// Screen pixels, by absolute physical position. `None` off the grab.
pub trait Pixels {
    fn rgb(&self, x: i32, y: i32) -> Option<[u8; 3]>;
}

/// Which corner of the selection the pointer is on.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Corner {
    BottomRight,
    TopLeft,
    BottomLeft,
    TopRight,
}

impl Corner {
    /// Most likely first: people mostly drag from top left to bottom right,
    /// then the other way round.
    pub const ALL: [Corner; 4] = [Corner::BottomRight, Corner::TopLeft, Corner::BottomLeft, Corner::TopRight];

    /// The `w` x `h` rectangle with the pointer on this corner. The pointer
    /// sits on the pixel just past a bottom or right edge, the way a drag's
    /// end point bounds a selection.
    pub fn rect(self, pointer: (i32, i32), w: i32, h: i32) -> Rect {
        let (px, py) = pointer;
        let (x, y) = match self {
            Corner::BottomRight => (px - w, py - h),
            Corner::TopLeft => (px, py),
            Corner::BottomLeft => (px, py - h),
            Corner::TopRight => (px - w, py),
        };
        Rect { x, y, w, h }
    }
}

/// Where a candidate rectangle comes from.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Source {
    Corner(Corner),
    /// The window under the pointer (a window snip).
    Window,
    /// The whole monitor (a full-screen snip).
    Monitor,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Candidate {
    pub source: Source,
    pub rect: Rect,
}

/// Every rectangle a `w` x `h` picture may have come from, likeliest first:
/// the four corners around the pointer, then the window under it and the
/// monitor, if their size matches the picture's (to a couple of pixels for
/// a window, whose frame Windows measures in its own way).
pub fn candidates(pointer: (i32, i32), w: i32, h: i32, window: Option<Rect>, monitor: Option<Rect>) -> Vec<Candidate> {
    let mut out: Vec<Candidate> =
        Corner::ALL.iter().map(|&c| Candidate { source: Source::Corner(c), rect: c.rect(pointer, w, h) }).collect();
    if let Some(win) = window.filter(|r| (r.w - w).abs() <= 2 && (r.h - h).abs() <= 2) {
        out.push(Candidate { source: Source::Window, rect: Rect { x: win.x, y: win.y, w, h } });
    }
    if let Some(mon) = monitor.filter(|r| r.w == w && r.h == h) {
        out.push(Candidate { source: Source::Monitor, rect: mon });
    }
    out
}

/// The screen area a search over `candidates` reads: all of them, with room
/// to nudge the corners.
pub fn search_area(candidates: &[Candidate]) -> Option<Rect> {
    candidates
        .iter()
        .map(|c| match c.source {
            Source::Corner(_) => c.rect.inflate(RADIUS + 1),
            _ => c.rect,
        })
        .reduce(|a, b| a.union(&b))
}

/// The picture, sampled on the comparison grid.
#[derive(Clone, Debug)]
pub struct Samples {
    pub w: u32,
    pub h: u32,
    points: Vec<(u32, u32)>,
    rgb: Vec<[u8; 3]>,
    /// How much the samples differ from their average (0 for a flat colour).
    /// A flat picture matches any flat stretch of screen, so its match says
    /// little.
    pub detail: f64,
}

impl Samples {
    /// Samples a `w` x `h` picture through `get(x, y)`.
    pub fn new(w: u32, h: u32, get: impl Fn(u32, u32) -> [u8; 3]) -> Samples {
        Samples::within(w, h, Rect { x: 0, y: 0, w: w as i32, h: h as i32 }, get)
    }

    /// Samples only `region` (in the picture's coordinates) of a `w` x `h`
    /// picture.
    pub fn within(w: u32, h: u32, region: Rect, get: impl Fn(u32, u32) -> [u8; 3]) -> Samples {
        let points: Vec<(u32, u32)> = grid_points(region.w.max(0) as u32, region.h.max(0) as u32)
            .into_iter()
            .map(|(x, y)| ((x as i32 + region.x) as u32, (y as i32 + region.y) as u32))
            .filter(|&(x, y)| x < w && y < h)
            .collect();
        let rgb: Vec<[u8; 3]> = points.iter().map(|&(x, y)| get(x, y)).collect();
        let n = rgb.len().max(1) as f64;
        let mean = |i: usize| rgb.iter().map(|p| p[i] as f64).sum::<f64>() / n;
        let m = [mean(0), mean(1), mean(2)];
        let detail = rgb.iter().map(|p| (0..3).map(|i| (p[i] as f64 - m[i]).abs()).sum::<f64>()).sum::<f64>() / n;
        Samples { w, h, points, rgb, detail }
    }
}

/// The grid's positions in a `w` x `h` picture: the centres of a
/// [`GRID`] x [`GRID`] split, so none lies on an edge (where Snipping Tool
/// may still be drawing the selection's outline).
pub fn grid_points(w: u32, h: u32) -> Vec<(u32, u32)> {
    if w == 0 || h == 0 {
        return Vec::new();
    }
    let at = |i: u32, n: u32| (((2 * i + 1) as u64 * n as u64) / (2 * GRID) as u64) as u32;
    (0..GRID).flat_map(|j| (0..GRID).map(move |i| (at(i, w), at(j, h)))).collect()
}

/// How well the screen at `at` (the top-left of a candidate) matches the
/// picture: 1.0 for the same pixels, toward 0.0 for unrelated ones. Samples
/// off the screen grab count as wholly different.
pub fn score(samples: &Samples, screen: &impl Pixels, at: (i32, i32)) -> f64 {
    score_above(samples, screen, at, f64::MIN).unwrap_or(0.0)
}

/// [`score`], giving up (`None`) as soon as the score is sure to end up
/// below `floor`: most places a search tries are wrong within a few dozen
/// samples.
fn score_above(samples: &Samples, screen: &impl Pixels, at: (i32, i32), floor: f64) -> Option<f64> {
    if samples.points.is_empty() {
        return None;
    }
    let total = CAP as u64 * samples.points.len() as u64;
    let budget = ((1.0 - floor).max(0.0) * total as f64).min(total as f64 + 1.0) as u64;
    let mut miss = 0u64;
    for (&(x, y), want) in samples.points.iter().zip(&samples.rgb) {
        miss += match screen.rgb(at.0 + x as i32, at.1 + y as i32) {
            Some(got) => (0..3).map(|i| (got[i] as i32 - want[i] as i32).unsigned_abs()).sum::<u32>().min(CAP) as u64,
            None => CAP as u64,
        };
        if miss > budget {
            return None;
        }
    }
    Some(1.0 - miss as f64 / total as f64)
}

/// The part of a candidate's picture within [`FOCUS`] (`half`) px of the
/// pointer, in the picture's coordinates: what a quick look compares. Every
/// candidate touches the pointer, so none comes out empty.
pub fn focus(pointer: (i32, i32), rect: Rect, half: i32) -> Option<Rect> {
    let around = Rect { x: pointer.0 - half, y: pointer.1 - half, w: 2 * half, h: 2 * half };
    rect.intersect(&around).map(|r| Rect { x: r.x - rect.x, y: r.y - rect.y, ..r })
}

/// The screen a quick look reads: the square around the pointer, with room
/// to nudge the corners.
pub fn focus_area(pointer: (i32, i32), half: i32) -> Rect {
    Rect { x: pointer.0 - half, y: pointer.1 - half, w: 2 * half, h: 2 * half }.inflate(RADIUS + 1)
}

/// The rectangle the picture was found at.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Found {
    pub rect: Rect,
    pub source: Source,
    pub score: f64,
    /// [`Samples::detail`] of what was compared: a plain patch matches
    /// plain screen anywhere, so its match says little.
    pub detail: f64,
}

/// Finds which of `candidates` (in [`candidates`]' order) the picture came
/// from, or `None` if nothing on `screen` matches well enough.
pub fn find(samples: &Samples, screen: &impl Pixels, candidates: &[Candidate]) -> Option<Found> {
    find_each(screen, candidates, |_| Some(samples.clone()))
}

/// [`find`], comparing each candidate through samples of its own (such as
/// the part of the picture near the pointer, see [`focus`]); a candidate
/// without samples is skipped.
pub fn find_each(
    screen: &impl Pixels,
    candidates: &[Candidate],
    samples_for: impl Fn(&Candidate) -> Option<Samples>,
) -> Option<Found> {
    let sampled: Vec<(&Candidate, Samples)> =
        candidates.iter().filter_map(|c| samples_for(c).map(|s| (c, s))).collect();
    let exact: Vec<Found> = sampled
        .iter()
        .map(|(c, samples)| Found {
            rect: c.rect,
            source: c.source,
            score: score(samples, screen, (c.rect.x, c.rect.y)),
            detail: samples.detail,
        })
        .collect();
    if let Some(found) = pick(&exact, SURE) {
        return Some(found);
    }
    // The pointer may have drifted a little before the clipboard update
    // came: try every place within RADIUS of each corner. Screenshots are
    // full of one-pixel detail (text), so nothing coarser would do; the
    // early give-up in score_above keeps it cheap.
    let mut all = exact;
    let mut floor = ACCEPT;
    for (c, samples) in sampled.iter().filter(|(c, _)| matches!(c.source, Source::Corner(_))) {
        let mut best: Option<Found> = None;
        for dy in -RADIUS..=RADIUS {
            for dx in -RADIUS..=RADIUS {
                let at = (c.rect.x + dx, c.rect.y + dy);
                if let Some(s) = score_above(samples, screen, at, floor) {
                    if best.is_none_or(|b| s > b.score) {
                        let rect = Rect { x: at.0, y: at.1, ..c.rect };
                        best = Some(Found { rect, source: c.source, score: s, detail: samples.detail });
                        // Anything worth keeping now has to tie with this.
                        floor = floor.max(s - TIE);
                    }
                }
            }
        }
        all.extend(best);
    }
    pick(&all, ACCEPT)
}

/// The best of `found` if it reaches `threshold`; among near ties, the
/// first (likeliest) one.
fn pick(found: &[Found], threshold: f64) -> Option<Found> {
    let best = found.iter().map(|f| f.score).fold(f64::MIN, f64::max);
    if best < threshold {
        return None;
    }
    found.iter().find(|f| f.score >= best - TIE).copied()
}

/// Where to fly from when the picture was not found on screen: a small
/// rectangle of the picture's shape, centred on the pointer, its long side
/// at most `long` px.
pub fn around_pointer(pointer: (i32, i32), w: i32, h: i32, long: i32) -> Rect {
    let s = (long as f64 / w.max(h).max(1) as f64).min(1.0);
    let (fw, fh) = (((w as f64 * s).round() as i32).max(1), ((h as f64 * s).round() as i32).max(1));
    Rect { x: pointer.0 - fw / 2, y: pointer.1 - fh / 2, w: fw, h: fh }
}

#[cfg(test)]
mod tests {
    use super::*;

    type Paint = Box<dyn Fn(i32, i32) -> [u8; 3]>;

    /// A made-up screen: busy, never-repeating content everywhere.
    struct Screen {
        area: Rect,
        /// Pixels replaced by something else (a picture, a flat patch).
        patch: Option<(Rect, Paint)>,
    }

    fn busy(x: i32, y: i32) -> [u8; 3] {
        let h = (x.wrapping_mul(73_856_093) ^ y.wrapping_mul(19_349_663)) as u32;
        let h = h ^ (h >> 13);
        [(h & 0xff) as u8, ((h >> 8) & 0xff) as u8, ((h >> 16) & 0xff) as u8]
    }

    impl Pixels for Screen {
        fn rgb(&self, x: i32, y: i32) -> Option<[u8; 3]> {
            if !self.area.contains(x, y) {
                return None;
            }
            if let Some((r, f)) = &self.patch {
                if r.contains(x, y) {
                    return Some(f(x, y));
                }
            }
            Some(busy(x, y))
        }
    }

    fn screen() -> Screen {
        Screen { area: Rect { x: 0, y: 0, w: 2880, h: 1800 }, patch: None }
    }

    /// The picture a snip of `r` on `screen` would give.
    fn snip(screen: &Screen, r: Rect) -> Samples {
        Samples::new(r.w as u32, r.h as u32, |x, y| screen.rgb(r.x + x as i32, r.y + y as i32).unwrap())
    }

    #[test]
    fn the_four_corners_put_the_pointer_on_each_corner() {
        let p = (500, 400);
        assert_eq!(Corner::BottomRight.rect(p, 100, 50), Rect { x: 400, y: 350, w: 100, h: 50 });
        assert_eq!(Corner::TopLeft.rect(p, 100, 50), Rect { x: 500, y: 400, w: 100, h: 50 });
        assert_eq!(Corner::BottomLeft.rect(p, 100, 50), Rect { x: 500, y: 350, w: 100, h: 50 });
        assert_eq!(Corner::TopRight.rect(p, 100, 50), Rect { x: 400, y: 400, w: 100, h: 50 });
        let c = candidates(p, 100, 50, None, None);
        assert_eq!(c.len(), 4);
        assert_eq!(c[0].source, Source::Corner(Corner::BottomRight));
    }

    #[test]
    fn a_window_or_monitor_of_the_same_size_joins_the_candidates() {
        let win = Rect { x: 10, y: 20, w: 801, h: 599 };
        let mon = Rect { x: 0, y: 0, w: 2880, h: 1800 };
        let c = candidates((50, 60), 800, 600, Some(win), Some(mon));
        assert_eq!(c.len(), 5);
        assert_eq!(c[4], Candidate { source: Source::Window, rect: Rect { x: 10, y: 20, w: 800, h: 600 } });
        let c = candidates((50, 60), 2880, 1800, Some(win), Some(mon));
        assert_eq!(c.last().unwrap().source, Source::Monitor);
        assert_eq!(candidates((50, 60), 640, 480, Some(win), Some(mon)).len(), 4);
    }

    #[test]
    fn the_search_area_covers_every_candidate_and_the_nudges() {
        let c = candidates((500, 400), 100, 50, None, None);
        let r = 13;
        assert_eq!(search_area(&c), Some(Rect { x: 400 - r, y: 350 - r, w: 200 + 2 * r, h: 100 + 2 * r }));
    }

    #[test]
    fn the_grid_stays_inside_the_picture() {
        let pts = grid_points(100, 7);
        assert_eq!(pts.len(), (GRID * GRID) as usize);
        assert!(pts.iter().all(|&(x, y)| x < 100 && y < 7));
        assert_eq!(pts[0], (1, 0));
        assert!(grid_points(0, 10).is_empty());
    }

    #[test]
    fn the_right_place_scores_one_and_others_far_less() {
        let s = screen();
        let r = Rect { x: 700, y: 300, w: 420, h: 260 };
        let samples = snip(&s, r);
        assert_eq!(score(&samples, &s, (700, 300)), 1.0);
        assert!(score(&samples, &s, (701, 300)) < 0.6);
        assert!(score(&samples, &s, (1120, 560)) < 0.6);
        // Half off the grab: those samples count as misses.
        assert!(score(&samples, &s, (2700, 300)) < 0.6);
    }

    #[test]
    fn small_differences_cost_little() {
        let s = screen();
        let r = Rect { x: 100, y: 100, w: 300, h: 200 };
        let samples = Samples::new(300, 200, |x, y| {
            let p = busy(100 + x as i32, 100 + y as i32);
            [p[0].saturating_add(3), p[1].saturating_sub(3), p[2]]
        });
        let score = score(&samples, &s, (r.x, r.y));
        assert!(score > 0.94 && score < 1.0, "{score}");
    }

    #[test]
    fn each_corner_is_found_where_the_pointer_says() {
        let s = screen();
        let r = Rect { x: 900, y: 500, w: 640, h: 360 };
        let samples = snip(&s, r);
        for (corner, pointer) in [
            (Corner::BottomRight, (r.x + r.w, r.y + r.h)),
            (Corner::TopLeft, (r.x, r.y)),
            (Corner::BottomLeft, (r.x, r.y + r.h)),
            (Corner::TopRight, (r.x + r.w, r.y)),
        ] {
            let found = find(&samples, &s, &candidates(pointer, r.w, r.h, None, None)).unwrap();
            assert_eq!(found.rect, r, "{corner:?}");
            assert_eq!(found.source, Source::Corner(corner));
            assert_eq!(found.score, 1.0);
        }
    }

    #[test]
    fn a_pointer_that_drifted_a_little_is_forgiven() {
        let s = screen();
        let r = Rect { x: 900, y: 500, w: 300, h: 180 };
        let samples = snip(&s, r);
        let pointer = (r.x + r.w + 7, r.y + r.h - 5);
        let found = find(&samples, &s, &candidates(pointer, r.w, r.h, None, None)).unwrap();
        assert_eq!(found.rect, r);
        assert_eq!(found.source, Source::Corner(Corner::BottomRight));
        // Too far off: nothing is claimed.
        let pointer = (r.x + r.w + 40, r.y + r.h);
        assert_eq!(find(&samples, &s, &candidates(pointer, r.w, r.h, None, None)), None);
    }

    #[test]
    fn a_picture_that_is_not_on_screen_is_not_found() {
        let s = screen();
        // Annotated, or the screen changed: other pixels altogether.
        let samples = Samples::new(300, 200, |x, y| busy(x as i32 * 7 + 3, y as i32 * 5 + 11));
        assert_eq!(find(&samples, &s, &candidates((1000, 800), 300, 200, None, None)), None);
    }

    #[test]
    fn a_window_snip_is_found_by_the_window_under_the_pointer() {
        let s = screen();
        let win = Rect { x: 320, y: 140, w: 900, h: 640 };
        let samples = snip(&s, win);
        // A window snip is a click inside the window, not a drag.
        let found = find(&samples, &s, &candidates((700, 400), 900, 640, Some(win), None)).unwrap();
        assert_eq!(found.source, Source::Window);
        assert_eq!(found.rect, win);
    }

    #[test]
    fn a_flat_picture_over_flat_screen_goes_by_the_likeliest_corner() {
        let flat = |_: i32, _: i32| [240, 240, 240];
        let mut s = screen();
        s.patch = Some((Rect { x: 0, y: 0, w: 2880, h: 900 }, Box::new(flat)));
        let samples = Samples::new(200, 100, |_, _| [240, 240, 240]);
        assert!(samples.detail < 1.0);
        let found = find(&samples, &s, &candidates((1000, 400), 200, 100, None, None)).unwrap();
        assert_eq!(found.source, Source::Corner(Corner::BottomRight));
        assert_eq!(found.rect, Rect { x: 800, y: 300, w: 200, h: 100 });
    }

    #[test]
    fn a_quick_look_compares_only_the_corner_near_the_pointer() {
        let r = Rect { x: 900, y: 500, w: 1600, h: 900 };
        let pointer = (r.x + r.w, r.y + r.h);
        // The pointer is on the bottom right: the picture's bottom-right patch.
        let br = Corner::BottomRight.rect(pointer, r.w, r.h);
        assert_eq!(focus(pointer, br, 256), Some(Rect { x: 1344, y: 644, w: 256, h: 256 }));
        // On the top left of its own rectangle for the top-left candidate.
        let tl = Corner::TopLeft.rect(pointer, r.w, r.h);
        assert_eq!(focus(pointer, tl, 256), Some(Rect { x: 0, y: 0, w: 256, h: 256 }));
        // A small picture is compared whole.
        let small = Corner::BottomRight.rect(pointer, 100, 60);
        assert_eq!(focus(pointer, small, 256), Some(Rect { x: 0, y: 0, w: 100, h: 60 }));
        assert_eq!(focus_area(pointer, 256), Rect { x: 2500 - 269, y: 1400 - 269, w: 538, h: 538 });
    }

    #[test]
    fn each_corner_is_found_from_the_patch_near_the_pointer() {
        let s = screen();
        let r = Rect { x: 700, y: 400, w: 1500, h: 1000 };
        let get = |x: u32, y: u32| busy(r.x + x as i32, r.y + y as i32);
        for (corner, pointer) in [
            (Corner::BottomRight, (r.x + r.w, r.y + r.h)),
            (Corner::TopLeft, (r.x, r.y)),
            (Corner::BottomLeft, (r.x, r.y + r.h)),
            (Corner::TopRight, (r.x + r.w, r.y)),
        ] {
            let cands = candidates(pointer, r.w, r.h, None, None);
            let found = find_each(&s, &cands, |c| {
                focus(pointer, c.rect, FOCUS).map(|f| Samples::within(r.w as u32, r.h as u32, f, get))
            })
            .unwrap();
            assert_eq!((found.rect, found.source), (r, Source::Corner(corner)));
            assert!(found.detail > PLAIN);
        }
    }

    #[test]
    fn the_fallback_is_a_small_rect_of_the_same_shape_on_the_pointer() {
        assert_eq!(around_pointer((500, 400), 1600, 900, 192), Rect { x: 404, y: 346, w: 192, h: 108 });
        // Already small: kept at its size.
        assert_eq!(around_pointer((500, 400), 40, 20, 192), Rect { x: 480, y: 390, w: 40, h: 20 });
    }
}
