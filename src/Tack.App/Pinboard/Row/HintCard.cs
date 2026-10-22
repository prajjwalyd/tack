using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tack.App.Pinboard.Motion;
using Tack.App.Pinboard.Prints;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// The empty board's index card, "Take a screenshot Â· Win + Shift + S",
/// pinned a little crooked in the middle of the felt. It fades and grows in a
/// moment after the board empties, and goes when a print arrives. It never
/// takes the pointer. While the keyboard drives an empty board, the card
/// wears the focus ring (the board's automation describes the empty board).
/// </summary>
sealed class HintCard
{
    /// <summary>The card's centre, down from the board element's top: the hidden part, half the visible board and 2.</summary>
    const double CentreY = BoardMetrics.TopHidden + BoardMetrics.BoardHeight / 2 + 2;

    const double HiddenScale = 0.97;
    const double FadeMs = 180;
    const double GrowMs = 260;
    const double ShowDelayMs = 120;

    readonly BoardContext _board;
    readonly Grid _area = new() { Height = 2 * CentreY, IsHitTestVisible = false };
    readonly Border _paper = new()
    {
        Padding = new Thickness(20, 15, 20, 14),
        CornerRadius = new CornerRadius(2),
    };
    readonly Ellipse _pin = new()
    {
        Width = BoardMetrics.PinSize,
        Height = BoardMetrics.PinSize,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 8.25 - BoardMetrics.PinSize / 2, 0, 0),
    };
    readonly TextBlock _lead = Words("Take a screenshot");
    readonly TextBlock _dot = Words("·");
    readonly TextBlock _keys = Words("Win + Shift + S");
    readonly KeyboardRing _ring = new();
    readonly Visual _visual;
    bool? _shown;

    public HintCard(BoardContext board)
    {
        _board = board;
        _dot.Margin = new Thickness(7, 0, 7, 0);
        _dot.Opacity = 0.55;
        // A plain weight: the font weight constants cannot be activated in an unpackaged app.
        _keys.FontWeight = new global::Windows.UI.Text.FontWeight(600);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(_lead);
        line.Children.Add(_dot);
        line.Children.Add(_keys);
        _paper.Child = line;
        var card = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform { Angle = -1 },
            RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
        };
        card.Children.Add(_paper);
        // The ring's hairline sits 6 out, a step further than a print's, so it clears the card's shadow.
        _ring.Element.Margin = new Thickness(-KeyboardRing.Outset - 1);
        _ring.Element.Visibility = Visibility.Collapsed;
        card.Children.Add(_ring.Element);
        card.Children.Add(_pin);
        _area.Children.Add(card);
        var children = board.Layers.Board.Children;
        children.Insert(children.IndexOf(board.Layers.Veil), _area);
        _visual = ElementCompositionPreview.GetElementVisual(_area);
        board.Keyframer.Set(_visual, Channel.Opacity, 0);
        board.Keyframer.Set(_visual, Channel.Scale, HiddenScale);
        ApplyTheme();
    }

    /// <summary>The board is <paramref name="width"/> wide: the card stays in its middle.</summary>
    public void Fit(double width)
    {
        _area.Width = width;
        _visual.CenterPoint = new Vector3((float)(width / 2), (float)CentreY, 0);
    }

    /// <summary>The keyboard's focus ring around the card (it shows only with the card).</summary>
    public void SetRinged(bool on) => _ring.Element.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shows the card on an empty board, or hides it. Changes at once while tucked.</summary>
    public void Show(bool on)
    {
        if (_shown == on)
        {
            return;
        }
        _shown = on;
        var (opacity, scale) = on ? (1.0, 1.0) : (0.0, HiddenScale);
        var keyframer = _board.Keyframer;
        if (!_board.State.Revealed)
        {
            keyframer.Set(_visual, Channel.Opacity, opacity);
            keyframer.Set(_visual, Channel.Scale, scale);
            return;
        }
        var clock = _board.Clock;
        var delay = on ? clock.Scaled(ShowDelayMs) : 0;
        keyframer.Transition(_visual, Channel.Opacity, opacity, clock.Scaled(FadeMs), CubicBezier.Ease, delay);
        keyframer.Transition(_visual, Channel.Scale, scale, clock.Scaled(GrowMs), CubicBezier.QuickOut, delay);
    }

    /// <summary>
    /// A transition cut short by a tuck stopped where it was: the card is put
    /// where it was going. Only once the motions are cancelled.
    /// </summary>
    public void Settle()
    {
        if (_shown is { } on)
        {
            _board.Keyframer.Set(_visual, Channel.Opacity, on ? 1 : 0);
            _board.Keyframer.Set(_visual, Channel.Scale, on ? 1 : HiddenScale);
        }
    }

    public void ApplyTheme()
    {
        var theme = _board.Theme;
        if (theme.IsHighContrast)
        {
            _paper.Background = theme.Brush(theme.Canvas);
            _paper.BorderBrush = theme.Brush(theme.CanvasText);
            _paper.BorderThickness = new Thickness(1);
            _lead.Foreground = _dot.Foreground = _keys.Foreground = theme.Brush(theme.CanvasText);
        }
        else
        {
            _paper.Background = theme.Brush(theme.IsDark ? BoardTheme.Rgb(0xe9, 0xe6, 0xdf) : BoardTheme.Rgb(0xfb, 0xfa, 0xf7));
            _paper.BorderThickness = new Thickness(0);
            _lead.Foreground = _dot.Foreground = theme.Brush(BoardTheme.Rgba(44, 34, 24, 0.62));
            _keys.Foreground = theme.Brush(BoardTheme.Rgba(44, 34, 24, 0.82));
        }
        _pin.Fill = theme.Brush(BoardTheme.Pin(PinColor.Sage));
        _pin.Stroke = theme.Brush(BoardTheme.PinShade(PinColor.Sage));
        _ring.ApplyTheme(theme);
    }

    static TextBlock Words(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI Variable Text"),
        FontSize = 13,
        LineHeight = 18,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        CharacterSpacing = 8,
    };
}
