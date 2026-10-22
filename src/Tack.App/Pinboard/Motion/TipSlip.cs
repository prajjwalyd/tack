using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Tack.App.Pinboard.Prints;
using Tack.Core.Geometry;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// The one-time tip after the first flight: a paper slip pinned under the
/// board's bottom edge saying Snipping Tool's own notification can be turned
/// off, with a close button. It swings in on its pin (fades in under reduced
/// motion); the app hears where it is, so the window takes clicks on it and
/// the board stays down to read it. It goes when closed, or at once on a tuck.
/// </summary>
sealed class TipSlip(BoardContext board)
{
    const double Width = 312;

    /// <summary>The slip hangs this far over the board's bottom edge.</summary>
    const double Overlap = 12;

    /// <summary>The slip's pin, down from its top: it turns about it.</summary>
    const double PinY = 9;

    const double CloseInset = 6;

    Grid? _slip;
    Visual? _visual;

    /// <summary>Shows the tip, unless it shows already or the board is up.</summary>
    public void Show()
    {
        if (_slip is not null || !board.State.Revealed)
        {
            return;
        }
        var reduced = board.State.Reduced;
        var slip = Build();
        var left = (board.State.WindowWidth - Width) / 2;
        var top = BoardMetrics.BoardHeight - Overlap;
        Canvas.SetLeft(slip, left);
        Canvas.SetTop(slip, top);
        board.Layers.Overlay.Children.Add(slip);
        slip.Measure(new global::Windows.Foundation.Size(Width, double.PositiveInfinity));
        var height = slip.DesiredSize.Height;
        ElementCompositionPreview.SetIsTranslationEnabled(slip, true);
        var visual = ElementCompositionPreview.GetElementVisual(slip);
        visual.CenterPoint = new Vector3((float)(Width / 2), (float)PinY, 0);
        (_slip, _visual) = (slip, visual);
        var angle = reduced ? 0 : TipMotions.RestAngle;
        board.Actions.SetTip(RectAt(left, top, height, angle));
        board.Keyframer.Set(visual, Channel.Rotation, angle);
        var motion = reduced ? TipMotions.ReducedIn(board.Clock) : TipMotions.In(board.Clock);
        board.Keyframer.Play(motion.Sample(), visual);
    }

    /// <summary>The close button: the slip lifts away, and the app hears it is gone.</summary>
    void Close()
    {
        if (_slip is not { } slip || _visual is not { } visual)
        {
            return;
        }
        (_slip, _visual) = (null, null);
        board.Actions.SetTip(null);
        var motion = TipMotions.Out(board.Clock, board.State.Reduced);
        board.Keyframer.Play(motion.Sample(), visual, () => board.Layers.Overlay.Children.Remove(slip));
    }

    /// <summary>The board tucks: the tip goes at once.</summary>
    public void Drop()
    {
        if (_slip is { } slip)
        {
            (_slip, _visual) = (null, null);
            board.Layers.Overlay.Children.Remove(slip);
            board.Actions.SetTip(null);
        }
    }

    /// <summary>Where the slip is at rest, turned about its pin: the box around it, physical px.</summary>
    Rect RectAt(double left, double top, double height, double angle)
    {
        var (sin, cos) = Math.SinCos(angle * Math.PI / 180);
        var (cx, cy) = (left + Width / 2, top + PinY);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (x, y) in new[] { (left, top), (left + Width, top), (left, top + height), (left + Width, top + height) })
        {
            var (dx, dy) = (x - cx, y - cy);
            var (rx, ry) = (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
            (minX, minY, maxX, maxY) = (Math.Min(minX, rx), Math.Min(minY, ry), Math.Max(maxX, rx), Math.Max(maxY, ry));
        }
        var scale = board.State.Scale;
        int Px(double v) => (int)Math.Round(v * scale, MidpointRounding.AwayFromZero);
        return new Rect(Px(minX), Px(minY), Px(maxX) - Px(minX), Px(maxY) - Px(minY));
    }

    Grid Build()
    {
        var theme = board.Theme;
        var contrast = theme.IsHighContrast;
        var art = PrintArt.ScaleFor(board.State.Scale);
        var ink = theme.Brush(contrast ? theme.CanvasText : theme.HintInk);
        var words = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Small"),
            FontSize = 12,
            LineHeight = 12 * 1.42,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            CharacterSpacing = 5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ink,
        };
        words.Inlines.Add(new Run { Text = "Tack shows your snips now. You can turn off Snipping Tool’s notifications in " });
        words.Inlines.Add(new Run
        {
            // Kept on one line, as one name.
            Text = "Settings › System › Notifications",
            FontWeight = new global::Windows.UI.Text.FontWeight(600),
            Foreground = theme.Brush(contrast ? theme.CanvasText : theme.HintInkStrong),
        });
        words.Inlines.Add(new Run { Text = "." });
        var paper = new Border
        {
            Padding = new Thickness(14, 16, 30, 12),
            CornerRadius = new CornerRadius(2),
            Background = theme.Brush(contrast ? theme.Canvas : theme.HintPaper),
            BorderBrush = contrast ? theme.Brush(theme.CanvasText) : null,
            BorderThickness = new Thickness(contrast ? 1 : 0),
            Child = words,
        };
        var slip = new Grid { Width = Width };
        AutomationProperties.SetName(slip, "Tip");
        if (!contrast)
        {
            foreach (var look in new[] { ShadowArt.Rest, ShadowArt.Lift })
            {
                var shadow = new NineGridShadow();
                shadow.Show(look, theme.IsDark, art);
                slip.Children.Add(shadow.Element);
                paper.SizeChanged += (_, e) => shadow.Resize(e.NewSize.Width, e.NewSize.Height);
            }
        }
        slip.Children.Add(paper);
        var pin = new PinArt();
        pin.Show(PinColor.Sage, theme.IsDark, art);
        ElementCompositionPreview.GetElementVisual(pin.Brass).Opacity = 0;
        pin.Root.HorizontalAlignment = HorizontalAlignment.Center;
        pin.Root.VerticalAlignment = VerticalAlignment.Top;
        pin.Root.Margin = new Thickness(0, PinY - BoardMetrics.PinSize / 2, 0, 0);
        slip.Children.Add(pin.Root);
        var close = new PrintButton(Icons.Cross(), danger: false);
        close.ApplyTheme(theme, art);
        close.Element.IsHitTestVisible = true;
        close.Element.Opacity = 0.72;
        close.Element.HorizontalAlignment = HorizontalAlignment.Right;
        close.Element.VerticalAlignment = VerticalAlignment.Top;
        close.Element.Margin = new Thickness(0, CloseInset, CloseInset, 0);
        close.Element.PointerEntered += (_, _) => close.Element.Opacity = 1;
        close.Element.PointerExited += (_, _) => close.Element.Opacity = 0.72;
        close.Element.Tapped += (_, _) => Close();
        AutomationProperties.SetName(close.Element, "Close");
        slip.Children.Add(close.Element);
        return slip;
    }
}
