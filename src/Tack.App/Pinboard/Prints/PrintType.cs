using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Tack.App.Pinboard.Prints;

/// <summary>The type on and under prints: Segoe UI Variable, its Text cut for notes and its Small cut for the small print.</summary>
static class PrintType
{
    public static FontFamily Text { get; } = new("Segoe UI Variable Text");

    public static FontFamily Small { get; } = new("Segoe UI Variable Small");

    /// <summary>
    /// A text block set on fixed lines of <paramref name="lineHeight"/>, spaced
    /// by <paramref name="spacing"/> px; it never takes the pointer.
    /// </summary>
    public static TextBlock Block(FontFamily family, double size, double lineHeight, double spacing) => new()
    {
        FontFamily = family,
        FontSize = size,
        LineHeight = lineHeight,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        // Character spacing is in thousandths of the font size.
        CharacterSpacing = (int)Math.Round(spacing / size * 1000),
        IsHitTestVisible = false,
    };
}
