using Tack.Core.Diagnostics;
using Tack.Windows.Display;
using Tack.Windows.Edge;
using Tack.Windows.Snips;

namespace Tack.App;

/// <summary>
/// Whether something full screen keeps the board up for a new print or a
/// notice. Windows is asked now: the poller's reading can be stale, and
/// Snipping Tool's own overlay, which covers the screen until moments before
/// a snip lands, never counts. A full-screen reading gets a moment to clear.
/// </summary>
sealed class FullScreenCheck(AppState state)
{
    /// <summary>How long a full-screen reading may take to clear before the board stays up.</summary>
    static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(1000);

    static readonly TimeSpan Recheck = TimeSpan.FromMilliseconds(100);

    /// <summary>True if the board must stay up; may take up to a second to say.</summary>
    public async Task<bool> HoldsBackAsync()
    {
        var giveUp = EdgePoller.Now + Grace;
        while (true)
        {
            var fullScreen = Monitors.FullScreenAppInFront();
            var snipping = fullScreen && SnippingTool.InFront();
            lock (state.Gate)
            {
                state.View.Fullscreen = fullScreen;
                state.View.Snipping = snipping;
            }
            if (!fullScreen || snipping)
            {
                return false;
            }
            if (EdgePoller.Now >= giveUp)
            {
                DevTrace.Write("reveal: the board stays up for a new print: something is full screen");
                return true;
            }
            await Task.Delay(Recheck).ConfigureAwait(false);
        }
    }
}
