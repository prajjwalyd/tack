using Tack.Core.Geometry;

namespace Tack.Core.Snips;

/// <summary>A rectangle a snip may have come from. <paramref name="Corner"/> is set for a corner candidate.</summary>
public readonly record struct Candidate(Source Source, Corner? Corner, Rect Rect);
