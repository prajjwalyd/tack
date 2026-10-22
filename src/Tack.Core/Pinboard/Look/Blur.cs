namespace Tack.Core.Pinboard.Look;

/// <summary>
/// The Gaussian blur of a straight edge, for drawing soft shadows as plain
/// gradients: a shape filled with a colour and blurred is, near one of its
/// edges, that colour at <see cref="Cover"/> of the distance in from the edge.
/// A shadow's blur length is twice the blur's standard deviation.
/// </summary>
public static class Blur
{
    /// <summary>
    /// How much of a blurred shape's colour is <paramref name="inside"/> logical px
    /// inside its edge (negative outside), 0 to 1, for a blur of <paramref name="sigma"/>.
    /// </summary>
    public static double Cover(double inside, double sigma) =>
        sigma <= 0 ? (inside >= 0 ? 1 : 0) : 0.5 * (1 + Erf(inside / (sigma * Math.Sqrt(2))));

    /// <summary>The error function, to within 1.5e-7 (Abramowitz and Stegun 7.1.26).</summary>
    public static double Erf(double x)
    {
        var sign = Math.Sign(x);
        x = Math.Abs(x);
        var t = 1 / (1 + 0.3275911 * x);
        var poly = ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t;
        return sign * (1 - poly * Math.Exp(-x * x));
    }
}
