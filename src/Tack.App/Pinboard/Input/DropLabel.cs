using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Tack.App.Pinboard.Feedback;
using Windows.Foundation;
using Windows.UI.Text;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// The drop cue's "Drop to pin" pill: a small down arrow onto a line, then the
/// words, on the notice's own frosted fill with its hairline edge and soft
/// shadows; plain system colours with a border under high contrast.
/// </summary>
sealed class DropLabel
{
    const double Height = 6 + 16 + 7;
    const double Icon = 12;

    readonly BoardTheme _theme;
    readonly Canvas _shadowHost = new();
    readonly Rectangle _fill = new() { RadiusX = Height / 2, RadiusY = Height / 2 };
    readonly Path _arrow = Arrow();
    readonly TextBlock _text = new()
    {
        Text = "Drop to pin",
        FontFamily = new FontFamily("Segoe UI Variable Text"),
        FontSize = 12.5,
        // Semibold; the FontWeights statics cannot be made in an unpackaged app.
        FontWeight = new FontWeight(600),
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        // 0.1 px at 12.5 px, in thousandths of an em.
        CharacterSpacing = 8,
        VerticalAlignment = VerticalAlignment.Center,
    };
    readonly NoticeShadow _shadow;

    public DropLabel(BoardTheme theme)
    {
        _theme = theme;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Padding = new Thickness(11, 6, 13, 7),
            Children = { _arrow, _text },
        };
        Element = new Grid
        {
            Height = Height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Children = { _shadowHost, _fill, row },
        };
        _shadow = new NoticeShadow(_shadowHost, _fill);
        Element.SizeChanged += (_, e) => _shadow.Resize(e.NewSize.Width, e.NewSize.Height);
    }

    public Grid Element { get; }

    public void ApplyTheme()
    {
        if (_theme.IsHighContrast)
        {
            _fill.Fill = _theme.Brush(_theme.Canvas);
            _fill.Stroke = _theme.Brush(_theme.CanvasText);
            _fill.StrokeThickness = 1;
            _text.Foreground = _arrow.Stroke = _theme.Brush(_theme.CanvasText);
            _shadow.Apply(visible: false, 1);
            return;
        }
        var dark = _theme.IsDark;
        var fillAlpha = dark ? 0.82 : 0.84;
        _fill.Fill = _theme.Brush(dark ? BoardTheme.Rgba(30, 34, 32, fillAlpha) : BoardTheme.Rgba(253, 252, 250, fillAlpha));
        _fill.Stroke = _theme.Brush(dark ? BoardTheme.Rgba(255, 255, 255, 0.10) : BoardTheme.Rgba(0, 0, 0, 0.08));
        _fill.StrokeThickness = 0.5;
        _text.Foreground = _arrow.Stroke = _theme.Brush(dark ? BoardTheme.Rgba(242, 246, 243, 0.92) : BoardTheme.Rgba(28, 32, 30, 0.88));
        _shadow.Apply(visible: true, fillAlpha);
    }

    /// <summary>A down arrow onto a line, on a 12 px grid, round ends.</summary>
    static Path Arrow()
    {
        var geometry = new PathGeometry();
        foreach (var points in new[]
        {
            new[] { (6.0, 2.2), (6.0, 7.8) },
            new[] { (3.6, 5.6), (6.0, 8.0), (8.4, 5.6) },
            new[] { (2.6, 9.8), (9.4, 9.8) },
        })
        {
            var figure = new PathFigure { StartPoint = new Point(points[0].Item1, points[0].Item2), IsClosed = false, IsFilled = false };
            foreach (var (x, y) in points.Skip(1))
            {
                figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
            }
            geometry.Figures.Add(figure);
        }
        return new Path
        {
            Width = Icon,
            Height = Icon,
            Data = geometry,
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
