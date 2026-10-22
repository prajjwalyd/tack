namespace Tack.Core.Pinboard.Look;

/// <summary>A colour and how opaque it is, 0 (clear) to 1 (opaque).</summary>
public readonly record struct Rgba(Rgb Rgb, double A)
{
    public Rgba(double r, double g, double b, double a) : this(new Rgb(r, g, b), a)
    {
    }

    /// <summary>Clear black.</summary>
    public static Rgba Clear { get; } = new(0, 0, 0, 0);

    /// <summary>The same colour, fully clear: what it fades to, so a fade never turns grey on the way.</summary>
    public Rgba Faded => this with { A = 0 };

    /// <summary>The same colour at <paramref name="factor"/> times its opacity.</summary>
    public Rgba Times(double factor) => this with { A = A * factor };

    public override string ToString() => $"{Rgb.ToHex()} at {A:0.###}";
}
