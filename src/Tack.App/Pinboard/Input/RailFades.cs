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
/// A fade is the board itself laid back over the prints: a strip just above
/// the rail shows a picture of the cork under it, through a gradient, so the
/// prints melt into the felt and frame at that side. The pictures are small
/// and only change with the cork, never as the prints move or the board
/// swings. When the row fits there is no clip and no strip. Should the
/// compositor refuse the pictures, the rail is only clipped, with no fades.
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
    readonly Visual _cork;
    readonly ContainerVisual _strips;
    readonly CompositionEasingFunction _ease;
    RectangleClip? _clip;
    Strip? _left;
    Strip? _right;
    bool _clipOnly;
    bool _on;
    ScrollEdges _edges;
    double _width;

    /// <summary>One side's fade: a sprite showing the cork beside it through a gradient whose inner stop slides in.</summary>
    sealed record Strip(SpriteVisual Sprite, CompositionVisualSurface Surface, CompositionColorGradientStop Inner);

    public RailFades(BoardContext board)
    {
        _board = board;
        var layers = board.Layers;
        _rail = ElementCompositionPreview.GetElementVisual(layers.Rail);
        _cork = ElementCompositionPreview.GetElementVisual(layers.Cork);
        _compositor = _rail.Compositor;
        _ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1));
        _strips = _compositor.CreateContainerVisual();
        _strips.IsVisible = false;
        // A host of its own just above the rail: under the veil and the drop cue, turning with the board.
        var host = new Canvas { IsHitTestVisible = false };
        Canvas.SetTop(host, Canvas.GetTop(layers.Rail));
        layers.Board.Children.Insert(layers.Board.Children.IndexOf(layers.Rail) + 1, host);
        ElementCompositionPreview.SetElementChildVisual(host, _strips);
    }

    /// <summary>
    /// The board is <paramref name="width"/> wide and the row overflows or
    /// not, with more to see on these <paramref name="edges"/>. The width is
    /// the board's own, never the rail's layout size: that lags a layout
    /// behind, and the strips kept a narrower board's place.
    /// </summary>
    public void Follow(double width, bool overflows, ScrollEdges edges)
    {
        if (width != _width)
        {
            Resize(width);
        }
        if (overflows != _on)
        {
            _on = overflows;
            Switch(overflows);
        }
        if (!_on || _left is null || _right is null || edges == _edges)
        {
            return;
        }
        _edges = edges;
        Fade(_left.Inner, edges.Left ? 1 : 0);
        Fade(_right.Inner, edges.Right ? 0 : 1);
    }

    /// <summary>On: the rail is clipped and the strips show, their fades starting from nothing. Off: neither.</summary>
    void Switch(bool on)
    {
        // Sized from the board's width: the rail's own visual can still have an older layout's size.
        _clip ??= _compositor.CreateRectangleClip(0, 0, (float)_width, (float)BoardMetrics.RailHeight);
        _rail.Clip = on ? _clip : null;
        if (on && !Strips())
        {
            return;
        }
        _edges = default;
        foreach (var (strip, rest) in new[] { (_left, 0f), (_right, 1f) })
        {
            if (strip is not null)
            {
                strip.Inner.StopAnimation(nameof(CompositionColorGradientStop.Offset));
                strip.Inner.Offset = rest;
                strip.Surface.SourceVisual = on ? _cork : null;
            }
        }
        _strips.IsVisible = on;
    }

    /// <summary>The two strips, made the first time the row overflows. False if they cannot be.</summary>
    bool Strips()
    {
        if (_clipOnly)
        {
            return false;
        }
        if (_left is not null)
        {
            return true;
        }
        try
        {
            _left = NewStrip(outer: 0, inner: 0);
            _right = NewStrip(outer: 1, inner: 1);
            Resize(_width);
            return true;
        }
        catch (Exception e)
        {
            DevTrace.Write($"board: no edge fades, only a clip: {e.Message}");
            (_left, _right, _clipOnly) = (null, null, true);
            _strips.Children.RemoveAll();
            return false;
        }
    }

    /// <summary>
    /// A strip whose cork shows fully at <paramref name="outer"/> (its side of
    /// the board) and not at all from the inner stop on, which rests at
    /// <paramref name="inner"/>, on the outer edge: no fade until it slides in.
    /// </summary>
    Strip NewStrip(float outer, float inner)
    {
        var surface = _compositor.CreateVisualSurface();
        var picture = _compositor.CreateSurfaceBrush(surface);
        picture.Stretch = CompositionStretch.Fill;
        var innerStop = _compositor.CreateColorGradientStop(inner, Colors.Transparent);
        var gradient = _compositor.CreateLinearGradientBrush();
        gradient.ColorStops.Add(_compositor.CreateColorGradientStop(outer, Colors.White));
        gradient.ColorStops.Add(innerStop);
        var masked = _compositor.CreateMaskBrush();
        masked.Source = picture;
        masked.Mask = gradient;
        var sprite = _compositor.CreateSpriteVisual();
        sprite.Brush = masked;
        _strips.Children.InsertAtTop(sprite);
        return new Strip(sprite, surface, innerStop);
    }

    void Resize(double width)
    {
        if (width <= 0)
        {
            return;
        }
        _width = width;
        if (_clip is not null)
        {
            _clip.Right = (float)width;
        }
        var size = new Vector2((float)Math.Min(FadePx, width / 2), (float)BoardMetrics.BoardHeight);
        // The rail's top is this far down the cork: the strips show the cork beside the visible rail.
        var top = (float)BoardMetrics.TopHidden;
        foreach (var (strip, x) in new[] { (_left, 0f), (_right, (float)width - size.X) })
        {
            if (strip is null)
            {
                continue;
            }
            strip.Sprite.Size = size;
            strip.Sprite.Offset = new Vector3(x, 0, 0);
            strip.Surface.SourceOffset = new Vector2(x, top);
            strip.Surface.SourceSize = size;
        }
    }

    void Fade(CompositionColorGradientStop stop, double to)
    {
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, (float)to, _ease);
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, _board.Clock.Scaled(FadeMs)));
        stop.StartAnimation(nameof(CompositionColorGradientStop.Offset), animation);
    }
}
