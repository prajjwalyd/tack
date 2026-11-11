using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Pinboard;
using Windows.UI.Text;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The name on a linked file's card, on one line under its thumbnail; the
/// thumbnail itself is the print's photo, shown whole (<see cref="PhotoFace.Fit"/>).
/// When the name is cut short, the full name shows in the caption on hover.
/// </summary>
sealed class FileFace
{
    const double LineHeight = 14;

    public FileFace()
    {
        Root = PrintType.Block(PrintType.Small, 11, LineHeight, 0.1);
        Root.FontWeight = new FontWeight(500);
        Root.TextWrapping = TextWrapping.NoWrap;
        Root.TextTrimming = TextTrimming.CharacterEllipsis;
        Root.MaxLines = 1;
        Root.TextAlignment = TextAlignment.Center;
        Root.VerticalAlignment = VerticalAlignment.Bottom;
        var side = BoardMetrics.PrintBorder + 2;
        Root.Margin = new Thickness(side, 0, side, (BoardMetrics.FileNameHeight - LineHeight) / 2);
    }

    /// <summary>The name's line, at the foot of the card.</summary>
    public TextBlock Root { get; }

    public void Show(string name) => Root.Text = name;

    /// <summary>The name didn't fit on its line and ends in an ellipsis (known once laid out).</summary>
    public bool Cut => Root.IsTextTrimmed;

    public void ApplyTheme(BoardTheme theme) =>
        Root.Foreground = theme.Brush(theme.IsHighContrast ? theme.CanvasText : theme.NoteInk);
}
