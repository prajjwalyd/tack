using System.Numerics;
using Microsoft.UI.Composition;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// The soft shade where the board tucks under the screen edge: solid over the
/// hidden part above the edge, then gone within 14 px below it, quickly at
/// first. Across the whole board, frame and all, over everything but the prints.
/// </summary>
sealed class TuckShade
{
    const double Hidden = BoardMetrics.TopHidden;
    const double Fade = 14;
    const double Length = Hidden + Fade;

    readonly CompositionLinearGradientBrush _brush;
    readonly SpriteVisual _sprite;

    public TuckShade(Compositor compositor)
    {
        _brush = Gradients.Down(compositor);
        _sprite = compositor.CreateSpriteVisual();
        _sprite.Brush = _brush;
        Visual = _sprite;
    }

    public Visual Visual { get; }

    public void Paint(Rgba shade)
    {
        // The shade fades out evenly below the edge, and a mask over it fades faster: .55 at 3 px, .18 at 8.
        static double Mask(double below) => below switch
        {
            <= 0 => 1,
            <= 3 => 1 - 0.45 * below / 3,
            <= 8 => 0.55 - 0.37 * (below - 3) / 5,
            _ => Math.Max(0, 0.18 * (Fade - below) / (Fade - 8)),
        };
        static double Cover(double y) => y <= Hidden ? 1 : (1 - (y - Hidden) / Fade) * Mask(y - Hidden);
        Gradients.SetStops(_brush, Gradients.Profile(shade.ToColor(), [0, .. Gradients.Steps(Hidden, Length, 15)], Length, Cover));
    }

    public void Resize(double width) => _sprite.Size = new Vector2((float)width, (float)Length);
}
