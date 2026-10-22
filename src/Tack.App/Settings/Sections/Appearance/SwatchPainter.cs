using Tack.App.Pinboard.Chrome;
using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;
using Windows.UI;

namespace Tack.App.Settings.Sections.Appearance;

/// <summary>
/// Paints each card's swatches with the board's own colour maths, so a swatch
/// shows what the board will be in this window's light or dark theme.
/// </summary>
static class SwatchPainter
{
    static readonly Color White = new Rgba(255, 255, 255, 1).ToColor();
    static readonly Color Shadow = new Rgba(0, 0, 0, 0.5).ToColor();
    static readonly Color PaperGlow = new Rgba(255, 255, 255, 0.7).ToColor();

    /// <summary>
    /// The board colours: each the felt the board would have, kept as dark as
    /// the board keeps it; Custom shows the colour wheel until a colour of the
    /// user's own is chosen, then that colour.
    /// </summary>
    public static void Felts(IReadOnlyList<SwatchFace> faces, Felt current, Rgb accent, bool dark)
    {
        for (var n = 0; n < faces.Count; n++)
        {
            var felt = FeltChoices.FeltOf(FeltChoices.All[n].Value) ?? current as Felt.CustomFelt;
            var picture = felt is null ? SwatchPictures.Wheel() : SwatchPictures.Dot(FeltTones.For(felt, accent, dark).Felt);
            faces[n].Paint(picture, White, Shadow);
        }
    }

    /// <summary>The frame finishes: a piece of each metal, its check mark in whichever ink reads on it.</summary>
    public static void Frames(IReadOnlyList<SwatchFace> faces, bool dark)
    {
        for (var n = 0; n < faces.Count; n++)
        {
            var swatch = FinishSwatch.For(new Frame(Frame.Presets[n]), dark);
            faces[n].Paint(SwatchPictures.Metal(swatch, dark), swatch.Ink.ToColor(), swatch.Glow.ToColor());
        }
    }

    /// <summary>The note papers, as the board's notes show them, with the notes' own ink for the check mark.</summary>
    public static void Papers(IReadOnlyList<SwatchFace> faces, bool dark)
    {
        for (var n = 0; n < faces.Count; n++)
        {
            var paper = PaperTones.For(new NotePaper(NotePaper.Presets[n]), dark).Paper;
            faces[n].Paint(SwatchPictures.Note(paper), PaperTones.Ink.ToColor(), PaperGlow);
        }
    }
}
