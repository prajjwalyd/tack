using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tack.Core.Diagnostics;
using Tack.Windows.Imaging;
using Tack.Windows.Windowing;

namespace Tack.App.Settings;

/// <summary>
/// Development builds only: opens Settings at startup on a chosen section and,
/// if asked, saves a picture of it, so its look can be checked without the
/// tray. The picture is the window alone, title bar and all, drawn by the
/// window itself into memory, never a grab of the screen, so nothing else on
/// screen can end up in it.
/// </summary>
/// <remarks>
/// TACK_DEBUG_SETTINGS=&lt;section&gt; opens it there ("top" for the top of the
/// list, "phone" for the phone page). TACK_DEBUG_SETTINGS_SHOT=&lt;file&gt; also
/// writes a PNG of it there once it has settled, and
/// TACK_DEBUG_SETTINGS_THEME=light or dark draws it in that theme
/// (scripts/dev/settings-shot.ps1 sets them).
/// </remarks>
static class DevSettingsShot
{
    static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1200);
    static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(500);

    static DispatcherQueueTimer? s_timer;

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Start(SettingsOpener opener, DispatcherQueue ui)
    {
        if (Environment.GetEnvironmentVariable("TACK_DEBUG_SETTINGS") is not { Length: > 0 } section)
        {
            return;
        }
        opener.Open(section == "top" ? null : section);
        if (opener.Window?.Content is FrameworkElement content && Environment.GetEnvironmentVariable("TACK_DEBUG_SETTINGS_THEME") is { } theme)
        {
            content.RequestedTheme = theme switch
            {
                "dark" => ElementTheme.Dark,
                "light" => ElementTheme.Light,
                _ => ElementTheme.Default,
            };
        }
        if (Environment.GetEnvironmentVariable("TACK_DEBUG_SETTINGS_SHOT") is not { Length: > 0 } file)
        {
            return;
        }
        s_timer = ui.CreateTimer();
        s_timer.Interval = Settle;
        s_timer.IsRepeating = false;
        s_timer.Tick += async (_, _) =>
        {
            try
            {
                await SaveAsync(opener.Window, file);
            }
            catch (Exception e)
            {
                DevTrace.Write($"settings picture failed: {e}");
            }
        };
        s_timer.Start();
    }

    static async Task SaveAsync(SettingsWindow? window, string file)
    {
        // The window's first frame can still be on its way: a black picture is taken again.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (window is null)
            {
                break;
            }
            if (WindowPicture.Of(window.Handle) is { } picture && !Blank(picture))
            {
                await File.WriteAllBytesAsync(file, await PictureEncoder.PngAsync(picture));
                var (at, size) = (window.AppWindow.Position, window.AppWindow.Size);
                DevTrace.Write($"settings picture: {file}, window at {at.X},{at.Y} size {size.Width}x{size.Height}");
                return;
            }
            await Task.Delay(Retry);
        }
        DevTrace.Write("settings picture: the window is not open or not drawn");
    }

    /// <summary>Nothing drawn: the middle of the window, which is never black, is.</summary>
    static bool Blank(Picture picture)
    {
        var at = ((picture.Height / 2 * picture.Width) + (picture.Width / 2)) * 4;
        return picture.Bgra[at] == 0 && picture.Bgra[at + 1] == 0 && picture.Bgra[at + 2] == 0;
    }
}
