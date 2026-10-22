using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;

namespace Tack.App.Pinboard.Feedback;

/// <summary>
/// The notice's two soft shadows, a close one 1 px down and a wide one 6 px
/// down drawn 4 px smaller all round, both shaped by the pill's own fill.
/// They are child visuals of an empty element behind the pill's fill, as
/// big as the pill.
/// </summary>
sealed class NoticeShadow
{
    readonly ContainerVisual _root;
    readonly (SpriteVisual Sprite, DropShadow Shadow, double Alpha, float Inset) _near;
    readonly (SpriteVisual Sprite, DropShadow Shadow, double Alpha, float Inset) _far;

    public NoticeShadow(UIElement host, Shape fill)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        var mask = fill.GetAlphaMask();
        if (mask is CompositionSurfaceBrush surface)
        {
            surface.Stretch = CompositionStretch.Fill;
        }
        _root = compositor.CreateContainerVisual();
        _far = (Sprite(compositor, mask, 6, 18, out var farShadow), farShadow, 0.26, 4);
        _near = (Sprite(compositor, mask, 1, 2, out var nearShadow), nearShadow, 0.12, 0);
        _root.Children.InsertAtTop(_far.Sprite);
        _root.Children.InsertAtTop(_near.Sprite);
        ElementCompositionPreview.SetElementChildVisual(host, _root);
    }

    /// <summary>Follows the pill's size.</summary>
    public void Resize(double width, double height)
    {
        foreach (var (sprite, _, _, inset) in new[] { _near, _far })
        {
            sprite.Offset = new Vector3(inset, inset, 0);
            sprite.Size = new Vector2(Math.Max(0, (float)width - 2 * inset), Math.Max(0, (float)height - 2 * inset));
        }
    }

    /// <summary>
    /// Shown or not (high contrast draws none). The mask is the fill, which is
    /// partly see-through, so each shadow is strengthened by its alpha to keep
    /// the shadow's own.
    /// </summary>
    public void Apply(bool visible, double fillAlpha)
    {
        _root.IsVisible = visible;
        foreach (var (_, shadow, alpha, _) in new[] { _near, _far })
        {
            shadow.Opacity = (float)Math.Min(1, alpha / Math.Max(fillAlpha, 0.01));
        }
    }

    static SpriteVisual Sprite(Compositor compositor, CompositionBrush mask, float y, float blur, out DropShadow shadow)
    {
        shadow = compositor.CreateDropShadow();
        shadow.Color = Colors.Black;
        shadow.Offset = new Vector3(0, y, 0);
        shadow.BlurRadius = blur;
        shadow.Mask = mask;
        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = shadow;
        return sprite;
    }
}
