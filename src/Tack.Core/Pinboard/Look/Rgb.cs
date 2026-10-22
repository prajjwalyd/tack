using System.Globalization;

namespace Tack.Core.Pinboard.Look;

/// <summary>
/// An opaque sRGB colour, each channel 0 to 255. Channels may hold fractions
/// (a colour laid over another, say) until <see cref="ToHex"/> or
/// <see cref="Byte"/> rounds them.
/// </summary>
public readonly record struct Rgb(double R, double G, double B)
{
    /// <summary>The colour of "#rrggbb", in any case.</summary>
    /// <exception cref="FormatException">Anything else.</exception>
    public static Rgb Parse(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#' || !hex.Skip(1).All(char.IsAsciiHexDigit))
        {
            throw new FormatException($"not a #rrggbb colour: {hex}");
        }
        double Channel(int at) => int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new(Channel(1), Channel(3), Channel(5));
    }

    /// <summary>A channel rounded and kept within 0 to 255.</summary>
    public static byte Byte(double channel) => (byte)Math.Clamp(Math.Round(channel, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>"#rrggbb" in lowercase, each channel rounded and kept within 0 to 255.</summary>
    public string ToHex() => $"#{Byte(R):x2}{Byte(G):x2}{Byte(B):x2}";

    /// <summary>This colour with every channel rounded to a whole number.</summary>
    public Rgb Rounded() => new(Byte(R), Byte(G), Byte(B));

    /// <summary>Each channel multiplied by its own factor (red, green, blue).</summary>
    public Rgb Times(double r, double g, double b) => new(R * r, G * g, B * b);

    /// <summary>Every channel multiplied by <paramref name="factor"/>.</summary>
    public Rgb Times(double factor) => Times(factor, factor, factor);

    /// <summary>This colour laid at <paramref name="alpha"/> (0 to 1) over the opaque <paramref name="under"/>.</summary>
    public Rgb Over(double alpha, Rgb under) =>
        new(R * alpha + under.R * (1 - alpha), G * alpha + under.G * (1 - alpha), B * alpha + under.B * (1 - alpha));

    /// <summary>
    /// <paramref name="share"/> (0 to 1) of this colour mixed with the rest of
    /// <paramref name="other"/>, channel by channel in sRGB.
    /// </summary>
    public Rgb Mix(double share, Rgb other) => Over(share, other);

    /// <summary>This colour at <paramref name="alpha"/>.</summary>
    public Rgba At(double alpha) => new(this, alpha);

    public override string ToString() => ToHex();
}
