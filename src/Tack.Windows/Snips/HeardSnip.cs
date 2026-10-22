namespace Tack.Windows.Snips;

/// <summary>A clipboard change judged to be a capture, as heard, waiting to be read.</summary>
/// <param name="Sequence">
/// The clipboard's sequence number then. The picture is taken only if it is
/// still the same once read, so it is the very copy that was judged.
/// </param>
/// <param name="Pointer">Where the pointer was then (physical px).</param>
readonly record struct HeardSnip(uint Sequence, (int X, int Y)? Pointer);
