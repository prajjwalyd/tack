using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Input;

/// <summary>How far the user has Windows scroll for a wheel notch, read afresh each time, as it can change at any moment.</summary>
public static unsafe class WheelSettings
{
    /// <summary>Windows' own default, for when the setting cannot be read.</summary>
    const int DefaultLines = 3;

    /// <summary>
    /// Lines per notch of the vertical wheel, or characters per notch of the
    /// sideways one (<paramref name="horizontal"/>); -1 when a notch scrolls a page.
    /// </summary>
    public static int Lines(bool horizontal)
    {
        uint lines;
        var action = horizontal
            ? SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWHEELSCROLLCHARS
            : SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWHEELSCROLLLINES;
        if (!PInvoke.SystemParametersInfo(action, 0, &lines, 0))
        {
            return DefaultLines;
        }
        // WHEEL_PAGESCROLL.
        return lines == uint.MaxValue ? -1 : (int)Math.Min(lines, 100);
    }
}
