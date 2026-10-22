using Microsoft.UI;
using Tack.Core.Pinboard.Look;
using Windows.UI;

namespace Tack.App.Pinboard.Chrome;

/// <summary>The look's colours as the compositor's and XAML's colours.</summary>
static class ToneColors
{
    public static Color ToColor(this Rgb colour) => colour.At(1).ToColor();

    public static Color ToColor(this Rgba tone) => ColorHelper.FromArgb(
        Rgb.Byte(tone.A * 255), Rgb.Byte(tone.Rgb.R), Rgb.Byte(tone.Rgb.G), Rgb.Byte(tone.Rgb.B));

    /// <summary>The colour of a Windows colour value, opaque.</summary>
    public static Rgb ToRgb(this Color colour) => new(colour.R, colour.G, colour.B);
}
