using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// A baked <see cref="ShadowArt"/> around a box: the picture's corners drawn
/// as they are and its edges stretched along the box, from the copy baked for
/// the screen's scale. A box smaller than the corners squeezes them. Its
/// element is the box's size; the shadow falls outside it. It never takes the pointer.
/// </summary>
sealed class NineGridShadow
{
    readonly CompositionSurfaceBrush _source;
    readonly CompositionNineGridBrush _grid;
    readonly SpriteVisual _sprite;
    ShadowArt? _art;
    double _scale = 1;
    double _w;
    double _h;

    public NineGridShadow()
    {
        Element = new Canvas { IsHitTestVisible = false };
        var compositor = ElementCompositionPreview.GetElementVisual(Element).Compositor;
        _source = compositor.CreateSurfaceBrush();
        _source.Stretch = CompositionStretch.Fill;
        _grid = compositor.CreateNineGridBrush();
        _grid.Source = _source;
        _grid.IsCenterHollow = true;
        _sprite = compositor.CreateSpriteVisual();
        _sprite.Brush = _grid;
        ElementCompositionPreview.SetElementChildVisual(Element, _sprite);
    }

    public Canvas Element { get; }

    /// <summary>Shows <paramref name="art"/> in a theme at a baked scale; null shows none (high contrast draws no shadows).</summary>
    public void Show(ShadowArt? art, bool dark, double scale)
    {
        (_art, _scale) = (art, scale);
        _source.Surface = art is null ? null : PrintArt.Surface(PrintArt.Themed(art.Name, dark), scale);
        Fit();
    }

    /// <summary>The box the shadow falls from, logical px.</summary>
    public void Resize(double w, double h)
    {
        (_w, _h) = (w, h);
        (Element.Width, Element.Height) = (w, h);
        Fit();
    }

    void Fit()
    {
        if (_art is not { } art)
        {
            return;
        }
        var margin = (float)art.Margin;
        var size = new Vector2((float)_w + 2 * margin, (float)_h + 2 * margin);
        _sprite.Offset = new Vector3(-margin, -margin, 0);
        _sprite.Size = size;
        // The grid's lines are in the picture's pixels; each is drawn its logical
        // width, or less when the box is too small for both corners.
        _grid.SetInsets((float)(art.Inset * _scale));
        var across = Squeeze(size.X, art.Inset) / (float)_scale;
        var down = Squeeze(size.Y, art.Inset) / (float)_scale;
        _grid.SetInsetScales(across, down, across, down);
    }

    static float Squeeze(float span, double inset) => (float)Math.Min(1, span / (2 * inset));
}
