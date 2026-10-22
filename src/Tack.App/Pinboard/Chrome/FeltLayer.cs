using System.Numerics;
using Microsoft.UI.Composition;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// The felt inside the frame, lit from above. Bottom up: the felt colour;
/// three wool pictures (white where the felt is lighter than its colour,
/// black where darker, so plain blending suits any colour) at tile sizes that
/// share no factor, so no repeat shows; the felt colour again at 1 - grain,
/// the wool's strength; the vignette towards the sides and bottom; the wash
/// of light from the top edge; and the crease where the felt meets the rails.
/// Clipped to the frame's inside, its bottom corners rounded.
/// </summary>
sealed class FeltLayer
{
    const double Height = BoardMetrics.ElementHeight - BoardMetrics.FrameWidth;
    const double SideCrease = 7;
    const double BottomCrease = 9;

    readonly RoundedClip _clip;
    readonly SpriteVisual _colour;
    readonly FeltTexture[] _wool;
    readonly SpriteVisual _fade;
    readonly SpriteVisual _vignette;
    readonly SpriteVisual _wash;
    readonly SpriteVisual _creaseLeft;
    readonly SpriteVisual _creaseRight;
    readonly SpriteVisual _creaseBottom;
    readonly CompositionColorBrush _colourBrush;
    readonly CompositionColorBrush _fadeBrush;
    readonly CompositionRadialGradientBrush _vignetteBrush;
    readonly CompositionRadialGradientBrush _washBrush;
    readonly CompositionLinearGradientBrush _creaseLeftBrush;
    readonly CompositionLinearGradientBrush _creaseRightBrush;
    readonly CompositionLinearGradientBrush _creaseBottomBrush;

    public FeltLayer(Compositor compositor)
    {
        Visual = compositor.CreateContainerVisual();
        Visual.Offset = new Vector3((float)BoardMetrics.FrameWidth, 0, 0);
        _clip = new RoundedClip(compositor, BoardMetrics.BoardRadius - BoardMetrics.FrameWidth);
        Visual.Clip = _clip.Clip;
        _colourBrush = compositor.CreateColorBrush();
        _fadeBrush = compositor.CreateColorBrush();
        _vignetteBrush = Radial(compositor, new(0.5f, 0.15f), new(1.4f, 1.2f));
        _washBrush = Radial(compositor, new(0.5f, 0), new(1.2f, 0.95f));
        _creaseLeftBrush = Gradients.Across(compositor);
        _creaseRightBrush = Gradients.Linear(compositor, Vector2.UnitX, Vector2.Zero);
        _creaseBottomBrush = Gradients.Linear(compositor, Vector2.UnitY, Vector2.Zero);
        _colour = Sprite(compositor, _colourBrush);
        _wool =
        [
            new FeltTexture(compositor, "felt/felt-cloud.png", 357),
            new FeltTexture(compositor, "felt/felt-fibre.png", 256),
            new FeltTexture(compositor, "felt/felt-pile.png", 193),
        ];
        foreach (var wool in _wool)
        {
            Visual.Children.InsertAtTop(wool.Visual);
        }
        _fade = Sprite(compositor, _fadeBrush);
        _vignette = Sprite(compositor, _vignetteBrush);
        _wash = Sprite(compositor, _washBrush);
        _creaseLeft = Sprite(compositor, _creaseLeftBrush);
        _creaseRight = Sprite(compositor, _creaseRightBrush);
        _creaseBottom = Sprite(compositor, _creaseBottomBrush);
    }

    public ContainerVisual Visual { get; }

    public void Paint(FeltTones tones)
    {
        _colourBrush.Color = tones.Felt.ToColor();
        _fadeBrush.Color = tones.Fade.ToColor();
        Gradients.SetStops(_vignetteBrush, [(0.52, tones.Vignette.Faded.ToColor()), (1, tones.Vignette.ToColor())]);
        Gradients.SetStops(_washBrush, [(0, tones.Wash.ToColor()), (0.72, tones.Wash.Faded.ToColor())]);
        foreach (var crease in new[] { _creaseLeftBrush, _creaseRightBrush, _creaseBottomBrush })
        {
            Gradients.SetStops(crease, [(0, tones.Crease.ToColor()), (1, tones.Crease.Faded.ToColor())]);
        }
    }

    /// <summary>For a board <paramref name="boardWidth"/> wide: the felt is that less the two side rails.</summary>
    public void Resize(double boardWidth)
    {
        var width = (float)(boardWidth - 2 * BoardMetrics.FrameWidth);
        var size = new Vector2(width, (float)Height);
        _clip.Resize(width, Height);
        _colour.Size = _fade.Size = _vignette.Size = _wash.Size = size;
        foreach (var wool in _wool)
        {
            wool.Resize(width);
        }
        _creaseLeft.Size = _creaseRight.Size = new Vector2((float)SideCrease, (float)Height);
        _creaseRight.Offset = new Vector3(width - (float)SideCrease, 0, 0);
        _creaseBottom.Size = new Vector2(width, (float)BottomCrease);
        _creaseBottom.Offset = new Vector3(0, (float)(Height - BottomCrease), 0);
    }

    SpriteVisual Sprite(Compositor compositor, CompositionBrush brush)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = brush;
        Visual.Children.InsertAtTop(sprite);
        return sprite;
    }

    /// <summary>A radial gradient centred at <paramref name="centre"/> with <paramref name="radius"/>, as fractions of the felt.</summary>
    static CompositionRadialGradientBrush Radial(Compositor compositor, Vector2 centre, Vector2 radius)
    {
        var brush = compositor.CreateRadialGradientBrush();
        (brush.EllipseCenter, brush.EllipseRadius) = (centre, radius);
        return brush;
    }
}
