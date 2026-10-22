using Tack.Core.Geometry;
using Tack.Windows.Edge;

namespace Tack.App;

/// <summary>
/// The one-time tip under the board about Snipping Tool's own notification,
/// shown after the first capture flies in. Once it shows it never shows again,
/// and it keeps the board down a while so it can be read; once it is closed
/// the board goes back up as usual.
/// </summary>
sealed class SnipTip(AppState state)
{
    /// <summary>
    /// How long the tip keeps the board down for reading, unless the pointer
    /// comes over it (and then leaves) first. Class D: text to read.
    /// </summary>
    static readonly TimeSpan Peek = TimeSpan.FromSeconds(9);

    /// <summary>The tip shows at <paramref name="rect"/> (physical px relative to the window) or, null, closed.</summary>
    public void Set(Rect? rect)
    {
        lock (state.Gate)
        {
            var view = state.View;
            if (!view.Shown)
            {
                return;
            }
            var now = EdgePoller.Now;
            view.Tip = rect;
            if (rect is null)
            {
                if (view.PeekUntil is not null)
                {
                    view.PeekUntil = now;
                }
                return;
            }
            // Never cuts short a longer peek already running.
            if ((view.PeekUntil ?? TimeSpan.Zero) < now + Peek)
            {
                view.PeekUntil = now + Peek;
            }
            if (!state.Settings.SnipTipShown)
            {
                state.Settings = state.Settings with { SnipTipShown = true };
                state.Save();
            }
        }
    }
}
