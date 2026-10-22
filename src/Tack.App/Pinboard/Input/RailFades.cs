using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Tack.Core.Diagnostics;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Input;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// The rail's edges while the row overflows: the rail is clipped at the
/// board's sides (never at its foot, so a falling print shows in the
/// overhang), and each side where more prints wait fades out over 44 px.
/// The fade is a mask over a picture of the rail: the rail itself is drawn
/// into a surface, shown through a gradient on a sprite just above it, and
/// left invisible but still taking the pointer. When the row fits there is
/// no picture, no mask and no clip, so nothing is drawn twice. Should the
/// compositor refuse the picture, the rail is only clipped, with no fades.
/// </summary>
sealed class RailFades
{
    /// <summary>How far in from a side the fade reaches, px.</summary>
    const double FadePx = 44;

    /// <summary>How long a fade takes to come or go, ms at Slow. Class A.</summary>
    const double FadeMs = 180;

    readonly BoardContext _board;
    readonly Compositor _compositor;
    readonly Visual _rail;
    readonly SpriteVisual _sprite;
    readonly CompositionLinearGradientBrush _mask;
    readonly CompositionColorGradientStop _leftInner;
    readonly CompositionColorGradientStop _rightInner;
    readonly CompositionEasingFunction _ease;
    CompositionVisualSurface? _surface;
    bool _clipOnly;
    bool _on;
    ScrollEdges _edges;
    double _width;

    public RailFades(BoardContext board)
    {
        _board = board;
        var layers = board.Layers;
        _rail = ElementCompositionPreview.GetElementVisual(layers.Rail);
        _compositor = _rail.Compositor;
        _ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1));

        _mask = _compositor.CreateLinearGradientBrush();
        _mask.ColorStops.Add(Stop(0, Colors.Transparent));
        _leftInner = Stop(0, Colors.White);
        _rightInner = Stop(1, Colors.White);
        _mask.ColorStops.Add(_leftInner);
        _mask.ColorStops.Add(_rightInner);
        _mask.ColorStops.Add(Stop(1, Colors.Transparent));

        _sprite = _compositor.CreateSpriteVisual();
        _sprite.IsVisible = false;
        // A host of its own just above the rail: under the veil and the drop cue, turning with the board.
        var host = new Canvas { IsHitTestVisible = false };
        Canvas.SetTop(host, Canvas.GetTop(layers.Rail));
        layers.Board.Children.Insert(layers.Board.Children.IndexOf(layers.Rail) + 1, host);
        ElementCompositionPreview.SetElementChildVisual(host, _sprite);
        layers.Rail.SizeChanged += (_, e) => Resize(e.NewSize.Width);
    }

    /// <summary>The row overflows or not, with more to see on these <paramref name="edges"/>.</summary>
    public void Follow(bool overflows, ScrollEdges edges)
    {
        if (overflows != _on)
        {
            _on = overflows;
            Switch(overflows);
        }
        if (!_on || _clipOnly || _width <= 0 || edges == _edges)
        {
            return;
        }
        _edges = edges;
        Fade(_leftInner, edges.Left ? FadePx / _width : 0);
        Fade(_rightInner, 1 - (edges.Right ? FadePx / _width : 0));
    }

    /// <summary>On: the rail goes into the masked picture, its fades starting from nothing. Off: the rail shows itself again.</summary>
    void Switch(bool on)
    {
        if (_clipOnly || (on && !Picture()))
        {
            _rail.Clip = on ? _compositor.CreateInsetClip() : null;
            return;
        }
        _edges = default;
        _leftInner.StopAnimation(nameof(CompositionColorGradientStop.Offset));
        _rightInner.StopAnimation(nameof(CompositionColorGradientStop.Offset));
        (_leftInner.Offset, _rightInner.Offset) = (0, 1);
        if (_surface is not null)
        {
            _surface.SourceVisual = on ? _rail : null;
        }
        _sprite.IsVisible = on;
        _rail.Opacity = on ? 0 : 1;
    }

    /// <summary>The surface the rail is drawn into, made the first time the row overflows. False if it cannot be.</summary>
    bool Picture()
    {
        if (_surface is not null)
        {
            return true;
        }
        try
        {
            _surface = _compositor.CreateVisualSurface();
            var picture = _compositor.CreateSurfaceBrush(_surface);
            picture.Stretch = CompositionStretch.Fill;
            var masked = _compositor.CreateMaskBrush();
            masked.Source = picture;
            masked.Mask = _mask;
            _sprite.Brush = masked;
            Resize(_board.Layers.Rail.ActualWidth);
            return true;
        }
        catch (Exception e)
        {
            DevTrace.Write($"board: no edge fades, only a clip: {e.Message}");
            (_surface, _clipOnly) = (null, true);
            return false;
        }
    }

    void Resize(double width)
    {
        if (width <= 0)
        {
            return;
        }
        _width = width;
        var size = new Vector2((float)width, (float)BoardMetrics.RailHeight);
        _sprite.Size = size;
        if (_surface is not null)
        {
            _surface.SourceSize = size;
        }
        if (_on && !_clipOnly)
        {
            // Offsets are fractions of the width: the fades keep their 44 px.
            _leftInner.StopAnimation(nameof(CompositionColorGradientStop.Offset));
            _rightInner.StopAnimation(nameof(CompositionColorGradientStop.Offset));
            _leftInner.Offset = _edges.Left ? (float)(FadePx / width) : 0;
            _rightInner.Offset = _edges.Right ? (float)(1 - FadePx / width) : 1;
        }
    }

    void Fade(CompositionColorGradientStop stop, double to)
    {
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, (float)to, _ease);
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, _board.Clock.Scaled(FadeMs)));
        stop.StartAnimation(nameof(CompositionColorGradientStop.Offset), animation);
    }

    CompositionColorGradientStop Stop(float offset, global::Windows.UI.Color color) => _compositor.CreateColorGradientStop(offset, color);
}
