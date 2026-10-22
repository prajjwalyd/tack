using Microsoft.UI.Dispatching;
using Tack.Core.Diagnostics;
using Tack.Core.Geometry;
using Tack.Core.Reveal;

namespace Tack.App;

/// <summary>
/// Bringing the board down and tucking it away. There is no board yet: each
/// request is posted to the UI thread, where the board will act on it, and is
/// only traced for now.
/// </summary>
sealed class Reveal(DispatcherQueue ui)
{
    /// <summary>The show-or-hide shortcut or a click on the tray icon: shows the board, or hides it if it is down.</summary>
    public void Toggle(RevealReason reason) => Post($"show or hide ({reason})");

    /// <summary>A new print is up: the board peeks out to show it, unless something is full screen.</summary>
    public void ForNew() => Post("peek for a new print");

    /// <summary>
    /// A new capture flies onto the board from <paramref name="from"/> (physical
    /// px), where it was taken, with <paramref name="picture"/> (a JPEG).
    /// </summary>
    public void FlyIn(Rect from, byte[] picture) => Post($"fly a {picture.Length} byte picture in from {from}");

    void Post(string what) => ui.TryEnqueue(() => DevTrace.Write($"reveal: {what}; there is no board yet"));
}
