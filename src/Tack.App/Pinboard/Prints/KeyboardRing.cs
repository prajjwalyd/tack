using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The keyboard's focus ring around a print's card, 4 px out: a bright
/// 2 px ring between two dark hairlines, so it reads on felt and paper in
/// both themes. High contrast draws one ring in the highlight colour.
/// </summary>
sealed class KeyboardRing
{
    /// <summary>How far the outer hairline sits outside the card.</summary>
    public const double Outset = 5;

    readonly Border _outer = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
    readonly Border _ring = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(7) };
    readonly Border _inner = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5) };

    public KeyboardRing()
    {
        _ring.Child = _inner;
        _outer.Child = _ring;
        _outer.IsHitTestVisible = false;
    }

    /// <summary>The ring, <see cref="Outset"/> larger than the card each side.</summary>
    public Border Element => _outer;

    public void Resize(double w, double h) => (_outer.Width, _outer.Height) = (w + 2 * Outset, h + 2 * Outset);

    public void ApplyTheme(BoardTheme theme)
    {
        var contrast = theme.IsHighContrast;
        var edge = contrast ? null : theme.Brush(theme.IsDark ? BoardTheme.Rgba(0, 0, 0, 0.72) : BoardTheme.Rgba(0, 20, 10, 0.66));
        _outer.BorderBrush = _inner.BorderBrush = edge;
        _ring.BorderBrush = theme.Brush(contrast ? theme.Highlight : theme.FocusRing);
    }
}
