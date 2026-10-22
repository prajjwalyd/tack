using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Motion;
using Windows.UI;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// "Here": while something pinnable is carried over the board, a soft wash of
/// light over the felt, a bright rim and a faint inner glow, the "Drop to pin"
/// label in the middle, and the prints a step back at 0.8. Within the frame,
/// under the veil, turning with the board; it never takes the pointer. Under
/// high contrast, a dashed highlight outline and a plain label.
/// </summary>
sealed class DropCue
{
    const double FadeMs = 160;
    const double LabelMs = 200;
    const double PrintsDimmed = 0.8;
    const double LabelY = 3;
    const double LabelScale = 0.97;
    const double Inset = BoardMetrics.FrameWidth;
    const double Height = BoardMetrics.BoardHeight - BoardMetrics.FrameWidth;
    const double Glow = 22;

    readonly BoardContext _board;
    readonly Grid _area = new() { Height = Height, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Border _wash = new();
    readonly Border _rim = new() { BorderThickness = new Thickness(1.5) };
    /// <summary>The glow's sides, left, top, right, bottom: where each sits and which way it fades.</summary>
    static readonly (HorizontalAlignment H, VerticalAlignment V, (double X, double Y) From, (double X, double Y) To)[] s_sides =
    [
        (HorizontalAlignment.Left, VerticalAlignment.Stretch, (0, 0.5), (1, 0.5)),
        (HorizontalAlignment.Stretch, VerticalAlignment.Top, (0.5, 0), (0.5, 1)),
        (HorizontalAlignment.Right, VerticalAlignment.Stretch, (1, 0.5), (0, 0.5)),
        (HorizontalAlignment.Stretch, VerticalAlignment.Bottom, (0.5, 1), (0.5, 0)),
    ];

    readonly Rectangle[] _glow = [.. s_sides.Select(side => new Rectangle
    {
        HorizontalAlignment = side.H,
        VerticalAlignment = side.V,
        Width = side.H == HorizontalAlignment.Stretch ? double.NaN : Glow,
        Height = side.V == VerticalAlignment.Stretch ? double.NaN : Glow,
    })];
    readonly Rectangle _dashes = new() { Margin = new Thickness(4), StrokeThickness = 2, StrokeDashArray = [3, 2] };
    readonly DropLabel _label;
    readonly Visual _visual;
    readonly Visual _labelVisual;
    readonly Visual _prints;
    bool _shown;

    public DropCue(BoardContext board)
    {
        _board = board;
        var radius = new CornerRadius(0, 0, BoardMetrics.BoardRadius - Inset, BoardMetrics.BoardRadius - Inset);
        _area.CornerRadius = _wash.CornerRadius = _rim.CornerRadius = radius;
        _area.Children.Add(_wash);
        foreach (var glow in _glow)
        {
            _area.Children.Add(glow);
        }
        _area.Children.Add(_rim);
        _area.Children.Add(_dashes);
        _label = new DropLabel(board.Theme);
        _area.Children.Add(_label.Element);
        Canvas.SetLeft(_area, Inset);
        Canvas.SetTop(_area, BoardMetrics.TopHidden);
        var children = board.Layers.Board.Children;
        children.Insert(children.IndexOf(board.Layers.Veil), _area);
        board.Layers.Rail.SizeChanged += (_, e) => _area.Width = Math.Max(0, e.NewSize.Width - 2 * Inset);
        _visual = ElementCompositionPreview.GetElementVisual(_area);
        ElementCompositionPreview.SetIsTranslationEnabled(_label.Element, true);
        _labelVisual = ElementCompositionPreview.GetElementVisual(_label.Element);
        _label.Element.SizeChanged += (_, e) =>
            _labelVisual.CenterPoint = new Vector3((float)(e.NewSize.Width / 2), (float)(e.NewSize.Height / 2), 0);
        _prints = ElementCompositionPreview.GetElementVisual(board.Layers.Track);
        Settle();
        ApplyTheme();
    }

    /// <summary>Shows the cue, or takes it away, easing either way.</summary>
    public void Show(bool on)
    {
        if (_shown == on)
        {
            return;
        }
        _shown = on;
        var keyframer = _board.Keyframer;
        var fade = _board.Clock.Scaled(FadeMs);
        if (on)
        {
            _area.Visibility = Visibility.Visible;
        }
        keyframer.Transition(_visual, Channel.Opacity, on ? 1 : 0, fade, CubicBezier.Ease, done: () =>
        {
            if (!_shown)
            {
                _area.Visibility = Visibility.Collapsed;
            }
        });
        keyframer.Transition(_prints, Channel.Opacity, on ? PrintsDimmed : 1, fade, CubicBezier.Ease);
        if (!_board.State.Reduced)
        {
            var label = _board.Clock.Scaled(LabelMs);
            keyframer.Transition(_labelVisual, Channel.Y, on ? 0 : LabelY, label, CubicBezier.QuickOut);
            keyframer.Transition(_labelVisual, Channel.Scale, on ? 1 : LabelScale, label, CubicBezier.QuickOut);
        }
    }

    /// <summary>Puts everything where it rests while no drag is over the board (after the motions are cancelled, or at the start).</summary>
    public void Settle()
    {
        _shown = false;
        _area.Visibility = Visibility.Collapsed;
        var keyframer = _board.Keyframer;
        keyframer.Set(_visual, Channel.Opacity, 0);
        keyframer.Set(_prints, Channel.Opacity, 1);
        var reduced = _board.State.Reduced;
        keyframer.Set(_labelVisual, Channel.Y, reduced ? 0 : LabelY);
        keyframer.Set(_labelVisual, Channel.Scale, reduced ? 1 : LabelScale);
    }

    public void ApplyTheme()
    {
        var theme = _board.Theme;
        var contrast = theme.IsHighContrast;
        _wash.Visibility = _rim.Visibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        _dashes.Visibility = contrast ? Visibility.Visible : Visibility.Collapsed;
        _dashes.Stroke = theme.Brush(theme.Highlight);
        var wash = theme.IsDark ? BoardTheme.Rgba(255, 255, 255, 0.09) : BoardTheme.Rgba(255, 255, 255, 0.16);
        _rim.BorderBrush = theme.Brush(theme.IsDark ? BoardTheme.Rgba(225, 255, 238, 0.36) : BoardTheme.Rgba(255, 255, 255, 0.62));
        _wash.Background = new RadialGradientBrush
        {
            Center = new global::Windows.Foundation.Point(0.5, 0.35),
            GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.35),
            RadiusX = 0.9,
            RadiusY = 1.2,
            GradientStops = { new GradientStop { Color = wash, Offset = 0 }, new GradientStop { Color = Clear(wash), Offset = 0.85 } },
        };
        Glows(contrast ? null : wash);
        _label.ApplyTheme();
    }

    /// <summary>
    /// The inner glow, a 22 px blur of the wash inside each edge: half the
    /// wash at the edge, a sixth at 11 px, none at 22, as the blur falls off.
    /// </summary>
    void Glows(Color? wash)
    {
        for (var i = 0; i < _glow.Length; i++)
        {
            _glow[i].Visibility = wash is null ? Visibility.Collapsed : Visibility.Visible;
            if (wash is not { } color)
            {
                continue;
            }
            var (from, to) = (s_sides[i].From, s_sides[i].To);
            _glow[i].Fill = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(from.X, from.Y),
                EndPoint = new global::Windows.Foundation.Point(to.X, to.Y),
                GradientStops =
                {
                    new GradientStop { Color = Scaled(color, 0.5), Offset = 0 },
                    new GradientStop { Color = Scaled(color, 0.16), Offset = 0.5 },
                    new GradientStop { Color = Clear(color), Offset = 1 },
                },
            };
        }
    }

    static Color Clear(Color color) => Scaled(color, 0);

    static Color Scaled(Color color, double share) => Color.FromArgb((byte)Math.Round(color.A * share), color.R, color.G, color.B);
}
