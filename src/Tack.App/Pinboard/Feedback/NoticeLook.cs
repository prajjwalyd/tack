using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tack.Core.Pinboard;
using Windows.UI.Text;

namespace Tack.App.Pinboard.Feedback;

/// <summary>
/// The notice's pill: one line of text, centred 14 px under the board, at
/// most 420 wide (and 16 short of each window edge), fully rounded, a light
/// or dark frosted fill with a hairline edge and two soft shadows; plain
/// system colours with a border under high contrast. It hangs in the overlay,
/// so it never turns with the board. UI thread only.
/// </summary>
sealed class NoticeLook
{
    const double Top = BoardMetrics.BoardHeight + 14;
    const double MaxWidth = 420;
    const double Margin = 16;
    const double Height = 6 + 16 + 7;

    readonly BoardTheme _theme;
    readonly Grid _row = new() { IsHitTestVisible = false };
    readonly Grid _pill = new() { HorizontalAlignment = HorizontalAlignment.Center, Height = Height };
    readonly Canvas _shadowHost = new();
    readonly Rectangle _fill = new() { RadiusX = Height / 2, RadiusY = Height / 2 };
    readonly TextBlock _text = new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Text"),
        FontSize = 12.5,
        // Medium; the FontWeights statics cannot be made in an unpackaged app.
        FontWeight = new FontWeight(500),
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        // 0.1 px at 12.5 px, in thousandths of an em.
        CharacterSpacing = 8,
        Padding = new Thickness(14, 6, 14, 7),
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
        VerticalAlignment = VerticalAlignment.Center,
    };
    readonly NoticeShadow _shadow;

    public NoticeLook(BoardTheme theme, Canvas overlay)
    {
        _theme = theme;
        _pill.Children.Add(_shadowHost);
        _pill.Children.Add(_fill);
        _pill.Children.Add(_text);
        _row.Children.Add(_pill);
        Canvas.SetTop(_row, Top);
        overlay.Children.Add(_row);
        _shadow = new NoticeShadow(_shadowHost, _fill);
        ElementCompositionPreview.SetIsTranslationEnabled(_pill, true);
        Visual = ElementCompositionPreview.GetElementVisual(_pill);
        _pill.SizeChanged += (_, e) =>
        {
            var (w, h) = (e.NewSize.Width, e.NewSize.Height);
            _shadow.Resize(w, h);
            // It grows in about its centre.
            Visual.CenterPoint = new Vector3((float)(w / 2), (float)(h / 2), 0);
        };
        overlay.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        Fit(overlay.ActualWidth);
        _row.Visibility = Visibility.Collapsed;
        ApplyTheme();
    }

    /// <summary>The pill's visual, for its motions: opacity, Translation.Y and Scale.</summary>
    public Visual Visual { get; }

    public bool Shown => _row.Visibility == Visibility.Visible;

    public void Show(string text)
    {
        _text.Text = text;
        _row.Visibility = Visibility.Visible;
    }

    public void Hide() => _row.Visibility = Visibility.Collapsed;

    /// <summary>Spans the window, so the pill centres in it, and keeps the pill clear of its edges.</summary>
    void Fit(double windowWidth)
    {
        if (windowWidth <= 0)
        {
            return;
        }
        _row.Width = windowWidth;
        _pill.MaxWidth = Math.Max(0, Math.Min(MaxWidth, windowWidth - 2 * Margin));
    }

    public void ApplyTheme()
    {
        var dark = _theme.IsDark;
        if (_theme.IsHighContrast)
        {
            _fill.Fill = _theme.Brush(_theme.Canvas);
            _fill.Stroke = _theme.Brush(_theme.CanvasText);
            _fill.StrokeThickness = 1;
            _text.Foreground = _theme.Brush(_theme.CanvasText);
            _shadow.Apply(visible: false, 1);
            return;
        }
        var fillAlpha = dark ? 0.82 : 0.84;
        _fill.Fill = _theme.Brush(dark ? BoardTheme.Rgba(30, 34, 32, fillAlpha) : BoardTheme.Rgba(253, 252, 250, fillAlpha));
        _fill.Stroke = _theme.Brush(dark ? BoardTheme.Rgba(255, 255, 255, 0.10) : BoardTheme.Rgba(0, 0, 0, 0.08));
        _fill.StrokeThickness = 0.5;
        _text.Foreground = _theme.Brush(dark ? BoardTheme.Rgba(242, 246, 243, 0.92) : BoardTheme.Rgba(28, 32, 30, 0.88));
        _shadow.Apply(visible: true, fillAlpha);
    }
}
