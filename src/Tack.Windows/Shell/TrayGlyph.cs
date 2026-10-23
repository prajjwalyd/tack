using Tack.Core.Diagnostics;
using Tack.Windows.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Shell;

/// <summary>
/// The tray icon's picture, as Tack 0.2 chose it: the glyph for the taskbar's
/// theme (dark on a light taskbar, light on a dark one), drawn for the tray's
/// size on the primary monitor, so Windows never has to rescale it. Picked
/// again when the theme or the scale changes. If a glyph will not load, the
/// app's .ico stands in, and Windows' generic icon if that will not either.
/// UI thread only.
/// </summary>
sealed unsafe class TrayGlyph : IDisposable
{
    readonly string _folder;
    readonly string _fallback;

    /// <summary>The glyph file last picked; null before the first.</summary>
    string? _picked;
    bool _owned;

    /// <param name="folder">Where the tray-light-*.png and tray-dark-*.png glyphs are.</param>
    /// <param name="fallback">The app's .ico, used when a glyph will not load.</param>
    public TrayGlyph(string folder, string fallback)
    {
        (_folder, _fallback) = (folder, fallback);
        Refresh();
    }

    /// <summary>The icon to show now.</summary>
    public HICON Icon { get; private set; }

    /// <summary>Picks the glyph for the theme and scale as they are now; true when that changed the icon.</summary>
    public bool Refresh()
    {
        var file = Path.Combine(_folder, FileName(TaskbarTheme.IsLight(), Monitors.At(0, 0).Scale));
        if (string.Equals(file, _picked, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var glyph = PngIcon.Load(file);
        var (icon, owned) = glyph.IsNull ? LoadFallback() : (glyph, true);
        DevTrace.Write($"tray: the {Path.GetFileName(file)} glyph{(glyph.IsNull ? " will not load; the .ico stands in" : "")}");
        Release();
        (Icon, _owned, _picked) = (icon, owned, file);
        return true;
    }

    /// <summary>
    /// The glyph for a taskbar theme and a primary monitor's scale. The tray
    /// draws icons 16 px at 100%, 20 at 125%, 24 at 150% and 32 at 200%; above
    /// that the 64 px glyph, which Windows shrinks.
    /// </summary>
    internal static string FileName(bool lightTaskbar, double scale)
    {
        var theme = lightTaskbar ? "tray-light" : "tray-dark";
        var pixels = Math.Round(16 * scale, MidpointRounding.AwayFromZero);
        return pixels switch
        {
            <= 16 => $"{theme}-16.png",
            <= 20 => $"{theme}-20.png",
            <= 24 => $"{theme}-24.png",
            <= 32 => $"{theme}.png",
            _ => $"{theme}@2x.png",
        };
    }

    /// <summary>The app's .ico at the small-icon size, or Windows' generic icon if it will not load.</summary>
    (HICON Icon, bool Owned) LoadFallback()
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        fixed (char* path = _fallback)
        {
            var loaded = PInvoke.LoadImage(HINSTANCE.Null, path, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE);
            if (!loaded.IsNull)
            {
                return ((HICON)(nint)loaded.Value, true);
            }
        }
        return (PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION), false);
    }

    void Release()
    {
        if (_owned)
        {
            PInvoke.DestroyIcon(Icon);
        }
        (Icon, _owned) = (HICON.Null, false);
    }

    public void Dispose() => Release();
}
