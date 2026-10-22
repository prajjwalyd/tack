namespace Tack.Core.Pinboard.Look;

/// <summary>Light and contrast the way WCAG measures them.</summary>
public static class Contrast
{
    /// <summary>The linear-light value (0 to 1) of an sRGB channel (0 to 255).</summary>
    public static double ToLinear(double channel)
    {
        var v = channel / 255;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>The sRGB channel (0 to 255, rounded) of a linear-light value (0 to 1).</summary>
    public static double FromLinear(double linear) =>
        Math.Round(255 * (linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055), MidpointRounding.AwayFromZero);

    /// <summary>Relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance(Rgb colour) =>
        0.2126 * ToLinear(colour.R) + 0.7152 * ToLinear(colour.G) + 0.0722 * ToLinear(colour.B);

    /// <summary>The contrast ratio (1 to 21) between two opaque colours.</summary>
    public static double Ratio(Rgb a, Rgb b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
