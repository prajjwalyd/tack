using Tack.Core.Geometry;

namespace Tack.Core.Snips;

/// <summary>Where a snip was found on screen.</summary>
/// <param name="Rect">Where the picture is.</param>
/// <param name="Source">Which kind of candidate it was.</param>
/// <param name="Corner">The corner the pointer was on, for a corner candidate.</param>
/// <param name="Score">1.0 for the very same pixels.</param>
/// <param name="Detail">How varied the compared samples were: a plain patch matches plain screen anywhere, so its match says little.</param>
public readonly record struct Found(Rect Rect, Source Source, Corner? Corner, double Score, double Detail);
