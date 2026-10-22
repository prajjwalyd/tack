using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Tack.Core.Notes;
using Windows.Foundation;
using Windows.UI.Text;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// A paper note's face: an index card's rules (a red header rule under the
/// pin, blue rules the text sits on), the text clamped to four lines, or a
/// link's address in blue on three with its domain small at the bottom, and a
/// faint shade where the free corner curls away from the light. High contrast
/// drops the rules and the curl and writes in system colours.
/// </summary>
sealed class NoteFace
{
    /// <summary>About four lines: the paper clamps the rest.</summary>
    const int MaxChars = 360;
    const double RedRule = 16;
    static readonly double[] BlueRules = [31, 46, 61, 76];

    readonly Border[] _rules;
    readonly TextBlock _text = PrintType.Block(PrintType.Text, 12, 15, 0.05);
    readonly Path _icon = Icons.Link();
    readonly TextBlock _domain = PrintType.Block(PrintType.Small, 10, 12, 0.3);
    readonly Grid _domainRow = new() { Margin = new Thickness(11, 0, 11, 8), VerticalAlignment = VerticalAlignment.Bottom, ColumnSpacing = 4 };
    readonly Border _curl = new() { IsHitTestVisible = false };
    bool _link;
    BoardTheme? _theme;
    NoteTones _tones;

    public NoteFace()
    {
        _rules = [.. new[] { RedRule }.Concat(BlueRules).Select(y => new Border
        {
            Height = 1,
            Margin = new Thickness(0, y, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        })];
        _text.Margin = new Thickness(11, 18, 11, 8);
        _text.VerticalAlignment = VerticalAlignment.Top;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.TextTrimming = TextTrimming.CharacterEllipsis;
        _domain.FontWeight = new FontWeight(600);
        _domain.TextTrimming = TextTrimming.CharacterEllipsis;
        _domain.TextWrapping = TextWrapping.NoWrap;
        _domain.MaxLines = 1;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _domainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _domainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_domain, 1);
        _domainRow.Children.Add(_icon);
        _domainRow.Children.Add(_domain);
        _curl.Background = new RadialGradientBrush
        {
            Center = new Point(1, 1),
            GradientOrigin = new Point(1, 1),
            RadiusX = 0.7,
            RadiusY = 0.6,
        };
        Root = new Grid { IsHitTestVisible = false };
        foreach (var rule in _rules)
        {
            Root.Children.Add(rule);
        }
        Root.Children.Add(_text);
        Root.Children.Add(_domainRow);
        Root.Children.Add(_curl);
    }

    /// <summary>The face, the paper's whole size.</summary>
    public Grid Root { get; }

    public void Show(NoteBody note)
    {
        _link = note.Link is not null;
        if (note.Link is { } link)
        {
            var bare = Address.Bare(link);
            _text.Text = Address.WithBreaks(bare);
            _domain.Text = note.Domain ?? bare.Split('/')[0];
        }
        else
        {
            _text.Text = Clamp(note.Text.Trim());
        }
        _text.MaxLines = _link ? 3 : 4;
        _domainRow.Visibility = _link ? Visibility.Visible : Visibility.Collapsed;
        Paint();
    }

    public void ApplyTheme(BoardTheme theme, NoteTones tones)
    {
        (_theme, _tones) = (theme, tones);
        Paint();
    }

    void Paint()
    {
        if (_theme is not { } theme)
        {
            return;
        }
        var tones = _tones;
        var contrast = theme.IsHighContrast;
        var red = theme.Brush(BoardTheme.Rgba(222, 72, 72, 0.62));
        var blue = theme.Brush(BoardTheme.Rgba(84, 140, 214, 0.24));
        for (var i = 0; i < _rules.Length; i++)
        {
            _rules[i].Background = i == 0 ? red : blue;
            _rules[i].Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        }
        _curl.Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        var stops = ((RadialGradientBrush)_curl.Background).GradientStops;
        stops.Clear();
        stops.Add(new GradientStop { Color = BoardTheme.Rgba(40, 46, 60, 0.06), Offset = 0 });
        stops.Add(new GradientStop { Color = BoardTheme.Rgba(40, 46, 60, 0), Offset = 0.7 });
        var link = theme.Brush(contrast ? theme.LinkText : tones.Link);
        _text.Foreground = _link ? link : theme.Brush(contrast ? theme.CanvasText : theme.NoteInk);
        _domain.Foreground = theme.Brush(contrast ? theme.LinkText : tones.Domain);
        _icon.Stroke = _domain.Foreground;
    }

    /// <summary>The first <see cref="MaxChars"/> characters, never splitting a pair of surrogates.</summary>
    static string Clamp(string text)
    {
        if (text.Length <= MaxChars)
        {
            return text;
        }
        var end = char.IsHighSurrogate(text[MaxChars - 1]) ? MaxChars - 1 : MaxChars;
        return text[..end];
    }
}
