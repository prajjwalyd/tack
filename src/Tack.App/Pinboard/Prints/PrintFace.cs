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
/// a linked file's thumbnail whole above its name, or a note's face (a
/// note's paper is the Note paper setting's colour, its corners a little
/// sharper). A note's or a file's own parts are made only for a print that
/// needs them. High contrast draws plain paper with an outline.
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
    NoteFace? _note;
    FileFace? _file;
    Kind _kind = Kind.Image;
    bool _dark;
    bool _contrast;
    double _artScale = 1;
    double _w;
    double _h;
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

    /// <summary>A photo's or a file's thumbnail (a note has none): a refreshed picture flashes in on it.</summary>
    public Image Picture => _photo.Picture;

    public PhotoFace Photo => _photo;

    bool IsNote => _kind == Kind.Note;

    /// <summary>Shows <paramref name="print"/>: its thumbnail, decoded off the UI thread, its note, or its file's thumbnail and name.</summary>
    public void Show(Print print)
    {
        if (print.Kind != _kind)
        {
            Become(print.Kind);
        }
        if (print.Note is { } note)
        {
            _note!.Show(note);
            return;
        }
        _file?.Show(print.Name);
        _photo.Show(print.Thumb);
    }

    /// <summary>The print's size, the paper border included, logical px.</summary>
    public void Resize(double w, double h)
    {
        (_w, _h) = (w, h);
        (Root.Width, Root.Height) = (w, h);
        _shadow.Resize(w, h);
        var below = _kind == Kind.File ? BoardMetrics.FileNameHeight : BoardMetrics.PrintBorder;
        _photo.Resize(Math.Max(0, w - 2 * BoardMetrics.PrintBorder), Math.Max(0, h - BoardMetrics.PrintBorder - below));
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

    /// <summary>Swaps what is on the paper for a print of another kind, making its parts the first time.</summary>
    void Become(Kind kind)
    {
        _kind = kind;
        _content.Children.Clear();
        switch (kind)
        {
            case Kind.Note:
                _note ??= new NoteFace();
                _content.Children.Add(_note.Root);
                break;
            case Kind.File:
                _file ??= new FileFace();
                _content.Children.Add(_photo.Root);
                _content.Children.Add(_file.Root);
                break;
            default:
                _content.Children.Add(_photo.Root);
                break;
        }
        _photo.Fit(whole: kind == Kind.File);
        Radius();
        Paint();
        Resize(_w, _h);
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
        _note?.ApplyTheme(theme, tones);
        _file?.ApplyTheme(theme);
        var paper = IsNote ? tones.Paper : theme.Paper;
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
        var radius = new CornerRadius(IsNote ? 2 : BoardMetrics.PrintRadius);
        _sheet.CornerRadius = _fibre.CornerRadius = _gloss.CornerRadius = radius;
    }

    void Shadow() => _shadow.Show(_contrast ? null : IsNote ? ShadowArt.Note : ShadowArt.Rest, _dark, _artScale);
}
