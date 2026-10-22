using Tack.Core.Appearance;

namespace Tack.Core.Pinboard.Look;

/// <summary>
/// A note's colours in one theme for the chosen note paper: the paper, and
/// the link and domain colours printed on it (a blue, darkened where the
/// paper would make it too faint). The ink, the rules and the curl sit on any
/// paper and do not change.
/// </summary>
/// <param name="Paper">The paper.</param>
/// <param name="Link">A link note's text.</param>
/// <param name="Domain">A link note's domain: the link's colour a little softer.</param>
public sealed record PaperTones(Rgb Paper, Rgb Link, Rgba Domain)
{
    /// <summary>A note's text: dark enough to read at 7:1 on every paper, in both themes.</summary>
    public static Rgba Ink { get; } = new(28, 32, 42, 0.86);

    /// <summary>A link's blue, so it differs from the ink.</summary>
    public static Rgb LinkBlue { get; } = Rgb.Parse("#2a4d8c");

    /// <summary>The link is darkened until it reads at least this well on the paper.</summary>
    public const double LinkMinContrast = 6.5;

    const double DomainAlpha = 0.8;

    /// <summary>The dark theme dims the paper a little: the light colour's channels times these (#fffefa to #eeebe4).</summary>
    static readonly double[] Dim = [0xee / (double)0xff, 0xeb / (double)0xfe, 0xe4 / (double)0xfa];

    /// <summary>The colours of <paramref name="paper"/> in the light or dark theme.</summary>
    public static PaperTones For(NotePaper paper, bool dark) => Of(Rgb.Parse(paper.Preset.Hex), dark);

    /// <summary>The colours of a paper that is <paramref name="light"/> in the light theme.</summary>
    public static PaperTones Of(Rgb light, bool dark)
    {
        var paper = dark ? Dimmed(light) : light;
        var link = LinkOn(paper);
        return new(paper, link, link.At(DomainAlpha));
    }

    /// <summary>The dark theme's paper, from the light one's.</summary>
    public static Rgb Dimmed(Rgb light) => light.Times(Dim[0], Dim[1], Dim[2]).Rounded();

    /// <summary>The link colour for <paramref name="paper"/>: the blue, darkened in steps until it reads.</summary>
    public static Rgb LinkOn(Rgb paper)
    {
        for (var dark = 0.0; dark < 1; dark += 0.02)
        {
            var link = LinkBlue.Times(1 - dark).Rounded();
            if (Contrast.Ratio(link, paper) >= LinkMinContrast)
            {
                return link;
            }
        }
        return new(0, 0, 0);
    }
}
