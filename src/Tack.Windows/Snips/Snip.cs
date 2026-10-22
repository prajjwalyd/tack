using Tack.Windows.Clipboard;
using Tack.Windows.Imaging;

namespace Tack.Windows.Snips;

/// <summary>A Snipping Tool capture, read off the clipboard.</summary>
/// <param name="Picture">The capture's pixels.</param>
/// <param name="Pointer">
/// Where the pointer was (physical px) the moment the capture arrived: normally
/// still on the corner of the selection where the drag ended. Null when Windows
/// would not say.
/// </param>
public sealed record Snip(Picture Picture, (int X, int Y)? Pointer);
