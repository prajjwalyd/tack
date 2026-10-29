using Tack.Core.Diagnostics;
using Tack.Windows.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Shell;

/// <summary>
/// The tray icon's picture: the app's .ico at the tray's size on the primary
/// monitor, which the .ico holds, so Windows never has to rescale it. Loaded
/// again when the scale changes; Windows' generic icon if the .ico will not
/// load. UI thread only.
/// </summary>
sealed unsafe class TrayGlyph : IDisposable
{
    readonly string _ico;

    /// <summary>The size last loaded, px; 0 before the first.</summary>
    int _size;
    bool _owned;

    /// <param name="ico">The app's .ico.</param>
    public TrayGlyph(string ico)
    {
        _ico = ico;
        Refresh();
    }

    /// <summary>The icon to show now.</summary>
    public HICON Icon { get; private set; }

    /// <summary>Loads the icon for the scale as it is now; true when that changed the icon.</summary>
    public bool Refresh()
    {
        var size = SizeFor(Monitors.At(0, 0).Scale);
        if (size == _size)
        {
            return false;
        }
        var (icon, owned) = Load(size);
        Release();
        (Icon, _owned, _size) = (icon, owned, size);
        return true;
    }

    /// <summary>
    /// The tray's icon size at a primary monitor's scale: 16 px at 100%, 20 at
    /// 125%, 24 at 150% and 32 at 200%, halves rounding up as Tack 0.2 rounded them.
    /// </summary>
    internal static int SizeFor(double scale) => (int)Math.Round(16 * scale, MidpointRounding.AwayFromZero);

    (HICON Icon, bool Owned) Load(int size)
    {
        fixed (char* path = _ico)
        {
            var loaded = PInvoke.LoadImage(HINSTANCE.Null, path, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE);
            if (!loaded.IsNull)
            {
                return ((HICON)(nint)loaded.Value, true);
            }
        }
        DevTrace.Write($"tray: {Path.GetFileName(_ico)} will not load; Windows' generic icon stands in");
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
