using Tack.Core.Appearance;

namespace Tack.Core.Pinboard.Look;

/// <summary>
/// The colours of the empty board's card and the one-time tip in one theme.
/// They are hints, not notes, cut from the same paper: a shade off it, with
/// their own warm brown ink at two strengths. White paper gives exactly the
/// colours the hint always had; a tinted or dimmed one gets stronger ink
/// where the same ink would read worse than on white (the sentence) or under
/// 7:1 (what it stresses).
/// </summary>
/// <param name="Paper">The card.</param>
/// <param name="Ink">The sentence.</param>
/// <param name="InkStrong">What the sentence stresses: the keys, the setting's path.</param>
public sealed record HintTones(Rgb Paper, Rgba Ink, Rgba InkStrong)
{
    /// <summary>The hint's ink colour; its strengths are alphas of it.</summary>
    public static Rgb InkColour { get; } = new(44, 34, 24);

    const double Soft = 0.62;
    const double Strong = 0.82;
    const double StrongMinContrast = 7;

    /// <summary>The card's paper as the note paper's channels times these: white's #fffefa to #fbfaf7.</summary>
    static readonly double[] OfNoteLight = [0xfb / (double)0xff, 0xfa / (double)0xfe, 0xf7 / (double)0xfa];

    /// <summary>And in the dark theme: #eeebe4 to #e9e6df.</summary>
    static readonly double[] OfNoteDark = [0xe9 / (double)0xee, 0xe6 / (double)0xeb, 0xdf / (double)0xe4];

    /// <summary>The hint's colours on <paramref name="paper"/> in the light or dark theme.</summary>
    public static HintTones For(NotePaper paper, bool dark) => Of(Rgb.Parse(paper.Preset.Hex), dark);

    /// <summary>The hint's colours on a note paper that is <paramref name="light"/> in the light theme.</summary>
    public static HintTones Of(Rgb light, bool dark)
    {
        var card = Cut(PaperTones.Of(light, dark).Paper, dark);
        var white = Cut(PaperTones.Of(Rgb.Parse(NotePaper.Presets[0].Hex), dark).Paper, dark);
        var softFloor = Contrast.Ratio(InkColour.Over(Soft, white), white);
        return new(card, InkColour.At(InkAlpha(Soft, card, softFloor)), InkColour.At(InkAlpha(Strong, card, StrongMinContrast)));
    }

    static Rgb Cut(Rgb paper, bool dark)
    {
        var of = dark ? OfNoteDark : OfNoteLight;
        return paper.Times(of[0], of[1], of[2]).Rounded();
    }

    /// <summary><paramref name="alpha"/>, raised in steps of .01 until the ink on <paramref name="paper"/> reads at <paramref name="min"/> (or is opaque).</summary>
    static double InkAlpha(double alpha, Rgb paper, double min)
    {
        while (alpha < 1 && Contrast.Ratio(InkColour.Over(alpha, paper), paper) < min)
        {
            alpha = Math.Round((alpha + 0.01) * 100, MidpointRounding.AwayFromZero) / 100;
        }
        return alpha;
    }
}
