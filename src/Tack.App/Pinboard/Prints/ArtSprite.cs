using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// One baked picture (a pin, a rim) on an element of its own, drawn at its
/// size in logical px from the copy baked for the screen's scale, so it stays
/// sharp. It never takes the pointer.
/// </summary>
sealed class ArtSprite
{
    readonly CompositionSurfaceBrush _brush;

    public ArtSprite(double width, double height)
    {
        Element = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
        var compositor = ElementCompositionPreview.GetElementVisual(Element).Compositor;
        _brush = compositor.CreateSurfaceBrush();
        _brush.Stretch = CompositionStretch.Fill;
        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = new Vector2((float)width, (float)height);
        sprite.Brush = _brush;
        ElementCompositionPreview.SetElementChildVisual(Element, sprite);
    }

    public Canvas Element { get; }

    /// <summary>Draws the picture <paramref name="name"/> baked at <paramref name="scale"/>; null draws nothing.</summary>
    public void Show(string? name, double scale) => _brush.Surface = name is null ? null : PrintArt.Surface(name, scale);
}
