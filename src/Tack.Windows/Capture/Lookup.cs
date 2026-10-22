using Tack.Core.Geometry;
using Tack.Core.Snips;

namespace Tack.Windows.Capture;

/// <summary>What <see cref="ScreenGrab.Find"/> concluded.</summary>
/// <param name="Found">Where the picture was found on screen, if it was.</param>
/// <param name="Rect">
/// Where the print flies from, in physical pixels: the place found, or else a
/// small rectangle of the picture's shape centred on the pointer.
/// </param>
/// <param name="Tries">Looks taken: 1 (the quick look settled it) or 2.</param>
/// <param name="Cpu">CPU time spent.</param>
/// <param name="Took">Time from start to answer, including the wait before a second look.</param>
public readonly record struct Lookup(Found? Found, Rect Rect, int Tries, TimeSpan Cpu, TimeSpan Took);
