using Tack.Core.Geometry;

namespace Tack.Core.Snips;

/// <summary>
/// Where on screen a snip was taken, worked out after the fact. When Snipping
/// Tool's clipboard update arrives, the pointer is normally still on one corner
/// of the selection and the picture is the selection's size, which leaves four
/// rectangles it can have come from, plus the window under the pointer or the
/// monitor when their size matches. Each is compared with the screen on a fixed
/// grid: the right one reads the very same pixels and scores 1.0. If none is
/// convincing (the pointer drifted), the corners are nudged within <see cref="Radius"/>.
/// No Windows APIs here: the Windows layer grabs the screen and calls in.
/// </summary>
public static class OriginSearch
{
    /// <summary>Half the side of the square around the pointer that a quick look compares.</summary>
    public const int FocusHalf = 256;

    /// <summary>Below this <see cref="Samples.Detail"/> a patch is too plain to tell candidates apart: compare the whole picture instead.</summary>
    public const double Plain = 6.0;

    /// <summary>An exact candidate this good ends the search at once.</summary>
    const double Sure = 0.97;

    /// <summary>The least a candidate must score to be believed at all.</summary>
    const double Accept = 0.86;

    /// <summary>How far, in physical px, a corner is nudged when the pointer moved since the drag ended.</summary>
    const int Radius = 12;

    /// <summary>Scores this close to the best count as a tie, settled by which candidate is likelier.</summary>
    const double Tie = 0.01;

    /// <summary>A sample's colour difference, summed over R, G and B, at which it counts as wholly different.</summary>
    const int Cap = 120;

    /// <summary>
    /// Every rectangle a <paramref name="w"/> x <paramref name="h"/> picture may have
    /// come from, likeliest first: the four corners around the pointer, then the
    /// window under it (within a couple of pixels, as Windows measures frames its
    /// own way) and the monitor, when their size matches.
    /// </summary>
    public static List<Candidate> Candidates((int X, int Y) pointer, int w, int h, Rect? window, Rect? monitor)
    {
        List<Candidate> candidates = [.. CornerRects.All.Select(c => new Candidate(Source.Corner, c, c.RectFor(pointer, w, h)))];
        if (window is { } win && Math.Abs(win.W - w) <= 2 && Math.Abs(win.H - h) <= 2)
        {
            candidates.Add(new Candidate(Source.Window, null, new Rect(win.X, win.Y, w, h)));
        }
        if (monitor is { } mon && mon.W == w && mon.H == h)
        {
            candidates.Add(new Candidate(Source.Monitor, null, mon));
        }
        return candidates;
    }

    /// <summary>The screen a search over <paramref name="candidates"/> reads: all of them, with room to nudge the corners.</summary>
    public static Rect? SearchArea(IReadOnlyList<Candidate> candidates) =>
        candidates.Count == 0 ? null
        : candidates.Select(c => c.Source == Source.Corner ? c.Rect.Inflate(Radius + 1) : c.Rect).Aggregate((a, b) => a.Union(b));

    /// <summary>The part of a candidate's picture within <paramref name="half"/> px of the pointer, in the picture's coordinates.</summary>
    public static Rect? Focus((int X, int Y) pointer, Rect rect, int half) =>
        rect.Intersect(new Rect(pointer.X - half, pointer.Y - half, 2 * half, 2 * half)) is { } r
            ? r with { X = r.X - rect.X, Y = r.Y - rect.Y }
            : null;

    /// <summary>The screen a quick look reads: the square around the pointer, with room to nudge the corners.</summary>
    public static Rect FocusArea((int X, int Y) pointer, int half) =>
        new Rect(pointer.X - half, pointer.Y - half, 2 * half, 2 * half).Inflate(Radius + 1);

    /// <summary>Which of <paramref name="candidates"/> the picture came from, or null if nothing on screen matches well enough.</summary>
    public static Found? Find(Samples samples, IPixels screen, IReadOnlyList<Candidate> candidates) =>
        FindEach(screen, candidates, _ => samples);

    /// <summary>As <see cref="Find"/>, comparing each candidate through samples of its own; one without samples is skipped.</summary>
    public static Found? FindEach(IPixels screen, IReadOnlyList<Candidate> candidates, Func<Candidate, Samples?> samplesFor)
    {
        var sampled = candidates.Select(c => (Candidate: c, Samples: samplesFor(c))).Where(s => s.Samples != null).ToList();
        List<Found> all = [.. sampled.Select(s => FoundAt(s.Candidate, s.Candidate.Rect, Score(s.Samples!, screen, (s.Candidate.Rect.X, s.Candidate.Rect.Y)), s.Samples!))];
        if (Pick(all, Sure) is { } sure)
        {
            return sure;
        }
        // The pointer may have drifted before the clipboard update came: try every
        // place within Radius of each corner. Screenshots are full of one-pixel
        // detail, so nothing coarser would do; giving up early keeps it cheap.
        var floor = Accept;
        foreach (var (candidate, samples) in sampled.Where(s => s.Candidate.Source == Source.Corner))
        {
            Found? best = null;
            for (var dy = -Radius; dy <= Radius; dy++)
            {
                for (var dx = -Radius; dx <= Radius; dx++)
                {
                    var at = (X: candidate.Rect.X + dx, Y: candidate.Rect.Y + dy);
                    if (ScoreAbove(samples!, screen, at, floor) is { } score && (best is null || score > best.Value.Score))
                    {
                        best = FoundAt(candidate, candidate.Rect with { X = at.X, Y = at.Y }, score, samples!);
                        // Anything worth keeping now has to tie with this.
                        floor = Math.Max(floor, score - Tie);
                    }
                }
            }
            if (best is { } found)
            {
                all.Add(found);
            }
        }
        return Pick(all, Accept);
    }

    /// <summary>
    /// Where to fly from when the picture was not found: a small rectangle of the
    /// picture's shape centred on the pointer, its long side at most <paramref name="longSide"/> px.
    /// </summary>
    public static Rect AroundPointer((int X, int Y) pointer, int w, int h, int longSide)
    {
        var s = Math.Min((double)longSide / Math.Max(Math.Max(w, h), 1), 1.0);
        var fw = Math.Max((int)Math.Round(w * s, MidpointRounding.AwayFromZero), 1);
        var fh = Math.Max((int)Math.Round(h * s, MidpointRounding.AwayFromZero), 1);
        return new Rect(pointer.X - fw / 2, pointer.Y - fh / 2, fw, fh);
    }

    /// <summary>How well the screen at <paramref name="at"/> matches: 1.0 for the same pixels. Samples off the grab are misses.</summary>
    internal static double Score(Samples samples, IPixels screen, (int X, int Y) at) =>
        ScoreAbove(samples, screen, at, double.MinValue) ?? 0.0;

    /// <summary>As <see cref="Score"/>, giving up (null) as soon as it is sure to end below <paramref name="floor"/>.</summary>
    static double? ScoreAbove(Samples samples, IPixels screen, (int X, int Y) at, double floor)
    {
        if (samples.Points.Count == 0)
        {
            return null;
        }
        var total = (ulong)Cap * (ulong)samples.Points.Count;
        var budget = (ulong)Math.Min(Math.Max(1.0 - floor, 0.0) * total, total + 1.0);
        var miss = 0UL;
        for (var i = 0; i < samples.Points.Count; i++)
        {
            var (x, y) = samples.Points[i];
            var want = samples.Colours[i];
            miss += screen.Get(at.X + x, at.Y + y) is { } got
                ? (ulong)Math.Min(Math.Abs(got.R - want.R) + Math.Abs(got.G - want.G) + Math.Abs(got.B - want.B), Cap)
                : Cap;
            if (miss > budget)
            {
                return null;
            }
        }
        return 1.0 - (double)miss / total;
    }

    static Found FoundAt(Candidate candidate, Rect rect, double score, Samples samples) =>
        new(rect, candidate.Source, candidate.Corner, score, samples.Detail);

    /// <summary>The best of <paramref name="found"/> if it reaches <paramref name="threshold"/>; among near ties, the first (likeliest).</summary>
    static Found? Pick(IReadOnlyList<Found> found, double threshold)
    {
        var best = found.Count == 0 ? double.MinValue : found.Max(f => f.Score);
        if (best < threshold)
        {
            return null;
        }
        return found.First(f => f.Score >= best - Tie);
    }
}
