using Tack.Core.Geometry;

namespace Tack.App.Pinboard.Contract;

/// <summary>A new capture's way onto the board: it flies from where it was taken.</summary>
/// <param name="From">
/// Where it was taken, logical px relative to the board's window. When not
/// <paramref name="Found"/>, a small rectangle of the picture's shape at the pointer.
/// </param>
/// <param name="Jpeg">The picture to fly with, sharper than the thumbnail: long side at most 1600 px.</param>
/// <param name="Found">The picture was found on screen, so <paramref name="From"/> is exactly where it was.</param>
/// <param name="Tip">Show the one-time tip about Snipping Tool's notification after it lands.</param>
/// <param name="MotionScale">The speed as it is now: the print can arrive before the reveal that carries it.</param>
sealed record Flight(LogicalRect From, byte[] Jpeg, bool Found, bool Tip, double MotionScale);
