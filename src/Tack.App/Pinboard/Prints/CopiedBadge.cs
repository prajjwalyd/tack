using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The copied check stamped on a print: a dark smoky disc with a faint light
/// rim and a soft shadow (baked), and a white check. High contrast draws it
/// in the highlight colours. Hidden at rest; its motion is the print motions'.
/// </summary>
sealed class CopiedBadge
{
    public const double Size = 28;
    const double RimMargin = 16;

    readonly ArtSprite _disc = new(Size + 2 * RimMargin, Size + 2 * RimMargin);
    readonly Border _plain = new() { Width = Size, Height = Size, CornerRadius = new CornerRadius(Size / 2), BorderThickness = new Thickness(1) };
    readonly Path _check = Icons.Check();

    public CopiedBadge()
    {
        Canvas.SetLeft(_disc.Element, -RimMargin);
        Canvas.SetTop(_disc.Element, -RimMargin);
        Element = new Canvas { Width = Size, Height = Size, IsHitTestVisible = false, Children = { _disc.Element, _plain, _check } };
    }

    public Canvas Element { get; }

    public void ApplyTheme(BoardTheme theme, double artScale)
    {
        var contrast = theme.IsHighContrast;
        _disc.Show(contrast ? null : "badge", artScale);
        _plain.Visibility = contrast ? Visibility.Visible : Visibility.Collapsed;
        _plain.Background = theme.Brush(theme.Highlight);
        _plain.BorderBrush = theme.Brush(theme.HighlightText);
        _check.Stroke = theme.Brush(contrast ? theme.HighlightText : BoardTheme.Rgb(255, 255, 255));
    }
}
