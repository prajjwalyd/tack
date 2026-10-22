using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tack.Core.Pinboard;
using Tack.Core.Prints;
using Windows.Foundation;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// The paper of a print and what is on it. The paper is photo paper: its
/// colour, a fine fibre and a soft fall of light from the top, with its rest
/// shadow on the felt; on it, a photo of the thumbnail inside an even border,
/// or a note's face (a note's paper is the Note paper setting's colour, its
/// corners a little sharper). High contrast draws plain paper with an outline.
/// </summary>
sealed class PrintFace
{
    readonly NineGridShadow _shadow = new();
    readonly Border _sheet = new();
    readonly Border _fibre = new();
    readonly Border _gloss = new();
    readonly LinearGradientBrush _light;
    readonly Grid _content = new();
    readonly PhotoFace _photo = new();
    readonly NoteFace _note = new();
    bool _isNote;
    bool _dark;
    bool _contrast;
    double _artScale = 1;
    BoardTheme? _theme;
    NoteTones _tones;

    public PrintFace()
    {
        _light = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = BoardTheme.Rgba(255, 255, 255, 0.5), Offset = 0 },
                new GradientStop { Color = BoardTheme.Rgba(255, 255, 255, 0), Offset = 0.45 },
                new GradientStop { Color = BoardTheme.Rgba(60, 40, 20, 0.035), Offset = 1 },
            },
        };
        _gloss.Child = _content;
        _fibre.Child = _gloss;
        _sheet.Child = _fibre;
        _content.Children.Add(_photo.Root);
        Root = new Grid { Children = { _shadow.Element, _sheet } };
        Radius();
    }

    /// <summary>The paper: the print's whole face, its shadow falling outside it.</summary>
    public Grid Root { get; }

    /// <summary>A photo's thumbnail (a note has none): a refreshed picture flashes in on it.</summary>
    public Image Picture => _photo.Picture;

    public PhotoFace Photo => _photo;

    public bool IsNote => _isNote;

    /// <summary>Shows <paramref name="print"/>: its thumbnail, decoded off the UI thread, or its note.</summary>
    public void Show(Print print)
    {
        var isNote = print.Note is not null;
        if (isNote != _isNote)
        {
            _isNote = isNote;
            _content.Children.Clear();
            _content.Children.Add(isNote ? _note.Root : _photo.Root);
            Radius();
            Paint();
        }
        if (print.Note is { } note)
        {
            _note.Show(note);
        }
        else
        {
            _photo.Show(print.Thumb);
        }
    }

    /// <summary>The print's size, the paper border included, logical px.</summary>
    public void Resize(double w, double h)
    {
        (Root.Width, Root.Height) = (w, h);
        _shadow.Resize(w, h);
        _photo.Resize(Math.Max(0, w - 2 * BoardMetrics.PrintBorder), Math.Max(0, h - 2 * BoardMetrics.PrintBorder));
    }

    /// <summary>The screen's scale, and the baked art's for it.</summary>
    public void Rescale(double scale, double artScale)
    {
        _photo.Rescale(scale);
        if (artScale != _artScale)
        {
            _artScale = artScale;
            Shadow();
        }
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
            Shadow();
            return;
        }
        var tones = _tones;
        (_dark, _contrast) = (theme.IsDark, theme.IsHighContrast);
        _photo.ApplyTheme(theme);
        _note.ApplyTheme(theme, tones);
        var paper = _isNote ? tones.Paper : theme.Paper;
        _sheet.Background = theme.Brush(_contrast ? theme.Canvas : paper);
        _sheet.BorderThickness = new Thickness(_contrast ? 1 : 0);
        _sheet.BorderBrush = _contrast ? theme.Brush(theme.CanvasText) : null;
        _fibre.Background = _contrast || PrintArt.Fibre is not { } fibre ? null : new ImageBrush
        {
            ImageSource = fibre,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
        _gloss.Background = _contrast ? null : _light;
        Shadow();
    }

    void Radius()
    {
        var radius = new CornerRadius(_isNote ? 2 : BoardMetrics.PrintRadius);
        _sheet.CornerRadius = _fibre.CornerRadius = _gloss.CornerRadius = radius;
    }

    void Shadow() => _shadow.Show(_contrast ? null : _isNote ? ShadowArt.Note : ShadowArt.Rest, _dark, _artScale);
}
