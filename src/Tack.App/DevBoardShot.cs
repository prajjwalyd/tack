using Tack.Core.Diagnostics;
using Tack.Core.Reveal;
using Tack.Windows.Capture;
using Tack.Windows.Display;
using Tack.Windows.Imaging;

namespace Tack.App;

/// <summary>
/// Development builds only: brings the board down at startup and, if asked,
/// saves a picture of it, so its look can be checked without pressing the
/// shortcut. Only the board's own rectangle (and a margin for its shadow) is
/// grabbed, never the rest of the screen.
/// </summary>
/// <remarks>
/// TACK_DEBUG_SHOW=1 shows the board and keeps it down. TACK_DEBUG_SHOT=&lt;file&gt;
/// also writes a PNG of it there once the swing has settled
/// (scripts/dev/board-shot.ps1 runs both).
/// </remarks>
static class DevBoardShot
{
    const int Margin = 12;
    static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1500);

    /// <param name="restored">Finishes once last session's prints are back, so the board comes down with them.</param>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Start(AppState state, Reveal reveal, Task restored)
    {
        if (Environment.GetEnvironmentVariable("TACK_DEBUG_SHOW") != "1")
        {
            return;
        }
        var file = Environment.GetEnvironmentVariable("TACK_DEBUG_SHOT");
        Background.Run("board picture", async () =>
        {
            await restored.ConfigureAwait(false);
            reveal.Show(RevealReason.Tray);
            if (file is { Length: > 0 })
            {
                await SaveAsync(state, file).ConfigureAwait(false);
            }
        });
    }

    static async Task SaveAsync(AppState state, string file)
    {
        await Task.Delay(Settle).ConfigureAwait(false);
        Core.Geometry.Rect? board;
        lock (state.Gate)
        {
            board = state.View.Shown ? state.View.BoardOnScreen() : null;
        }
        // On the monitor only: above the screen edge there is nothing but black.
        var grab = board is { } area ? area.Inflate(Margin).Intersect(Monitors.At(area.X, area.Y).Bounds) : null;
        if (grab is not { } rect || ScreenGrab.GrabPicture(rect) is not { } picture)
        {
            DevTrace.Write("board picture: the board is not down");
            return;
        }
        await File.WriteAllBytesAsync(file, await PictureEncoder.PngAsync(picture).ConfigureAwait(false)).ConfigureAwait(false);
        DevTrace.Write($"board picture: {file}");
    }
}
