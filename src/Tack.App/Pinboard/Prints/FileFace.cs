using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Words;
using Windows.UI.Text;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The name on a linked file's card, on one line under its thumbnail, cut in
/// the middle when it is too long so its start and extension both show
/// (<see cref="FileNameFit"/>); the thumbnail itself is the print's photo,
/// shown whole (<see cref="PhotoFace.Fit"/>).
/// </summary>
sealed class FileFace
{
    const double LineHeight = 14;

    /// <summary>A line set as the name's, never shown, to measure cut names on.</summary>
    static TextBlock? s_ruler;

    string _name = "";

    public FileFace()
    {
        Root = Line();
        Root.TextWrapping = TextWrapping.NoWrap;
        Root.TextTrimming = TextTrimming.CharacterEllipsis;
        Root.MaxLines = 1;
        Root.TextAlignment = TextAlignment.Center;
        Root.VerticalAlignment = VerticalAlignment.Bottom;
        var side = BoardMetrics.PrintBorder + 2;
        Root.Margin = new Thickness(side, 0, side, (BoardMetrics.FileNameHeight - LineHeight) / 2);
        // The line spans the card, so its width is the room the name has.
        Root.SizeChanged += (_, e) => Fit(e.NewSize.Width);
    }

    /// <summary>The name's line, at the foot of the card.</summary>
    public TextBlock Root { get; }

    public void Show(string name)
    {
        _name = name;
        Fit(Root.ActualWidth);
    }

    public void ApplyTheme(BoardTheme theme) =>
        Root.Foreground = theme.Brush(theme.IsHighContrast ? theme.CanvasText : theme.NoteInk);

    /// <summary>
    /// The name as it fits a line <paramref name="width"/> wide (the whole
    /// name before the line is laid out), a pixel to spare so rounding never
    /// trims its end as well.
    /// </summary>
    void Fit(double width) => Root.Text = FileNameFit.Fit(_name, width > 1 ? width - 1 : 0, Measure);

    static double Measure(string text)
    {
        var ruler = s_ruler ??= Line();
        ruler.Text = text;
        ruler.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return ruler.DesiredSize.Width;
    }

    static TextBlock Line()
    {
        var line = PrintType.Block(PrintType.Small, 11, LineHeight, 0.1);
        line.FontWeight = new FontWeight(500);
        return line;
    }
}
