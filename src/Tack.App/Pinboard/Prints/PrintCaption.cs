using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Text;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The small print under a print, centred below it: its age in light,
/// embossed type on the felt, and for a link note its whole address above
/// (two lines at most, kept narrow enough to stay on the felt). The age's
/// words come from whoever keeps captions fresh, through <see cref="SetAge"/>.
/// High contrast sets it in system colours on a plain background.
/// </summary>
sealed class PrintCaption
{
    /// <summary>The caption's box, wider than any print, centred under it; the words are centred in it.</summary>
    const double BoxWidth = 240;
    const double LinkWidth = 184;
    const double Gap = 5;

    readonly TextBlock _url = Line(13, 500);
    readonly TextBlock _urlEmboss = Line(13, 500);
    readonly TextBlock _age = Line(14, 600);
    readonly TextBlock _ageEmboss = Line(14, 600);
    readonly Grid _urlPair;
    readonly StackPanel _words = new() { HorizontalAlignment = HorizontalAlignment.Center };

    public PrintCaption()
    {
        foreach (var url in new[] { _url, _urlEmboss })
        {
            url.MaxLines = 2;
            url.TextWrapping = TextWrapping.Wrap;
            url.TextTrimming = TextTrimming.CharacterEllipsis;
            url.MaxWidth = LinkWidth;
        }
        _urlPair = Pair(_urlEmboss, _url);
        _words.Children.Add(_urlPair);
        _words.Children.Add(Pair(_ageEmboss, _age));
        Element = new Border { Width = BoxWidth, IsHitTestVisible = false, Child = _words };
    }

    /// <summary>The caption's box.</summary>
    public Border Element { get; }

    /// <summary>Places it under a print <paramref name="w"/> by <paramref name="h"/>, in the slot.</summary>
    public void Place(double w, double h)
    {
        Canvas.SetLeft(Element, (w - BoxWidth) / 2);
        Canvas.SetTop(Element, h + Gap);
    }

    /// <summary>The age, as said in words ("2 minutes ago").</summary>
    public void SetAge(string text) => _age.Text = _ageEmboss.Text = text;

    /// <summary>A link note's full address over its age; null for none.</summary>
    public void SetLink(string? url)
    {
        var text = url is null ? "" : Address.WithBreaks(url);
        _url.Text = _urlEmboss.Text = text;
        _urlPair.Visibility = url is null ? Visibility.Collapsed : Visibility.Visible;
        _age.LineHeight = _ageEmboss.LineHeight = url is null ? 14 : 13;
    }

    public void ApplyTheme(BoardTheme theme)
    {
        var contrast = theme.IsHighContrast;
        _url.Foreground = _age.Foreground = theme.Brush(contrast ? theme.CanvasText : theme.CaptionInk);
        _urlEmboss.Foreground = _ageEmboss.Foreground = theme.Brush(BoardTheme.Rgba(0, 10, 4, 0.45));
        _urlEmboss.Visibility = _ageEmboss.Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        _words.Background = contrast ? theme.Brush(theme.Canvas) : null;
        _words.Padding = new Thickness(contrast ? 3 : 0, 0, contrast ? 3 : 0, 0);
    }

    static TextBlock Line(double lineHeight, ushort weight)
    {
        var line = PrintType.Block(PrintType.Small, 11, lineHeight, 0.15);
        line.FontWeight = new FontWeight(weight);
        line.TextAlignment = TextAlignment.Center;
        line.HorizontalAlignment = HorizontalAlignment.Center;
        return line;
    }

    /// <summary>The words over their emboss, a light shadow 1 px below.</summary>
    static Grid Pair(TextBlock emboss, TextBlock words)
    {
        emboss.Margin = new Thickness(0, 1, 0, -1);
        return new Grid { HorizontalAlignment = HorizontalAlignment.Center, Children = { emboss, words } };
    }
}
