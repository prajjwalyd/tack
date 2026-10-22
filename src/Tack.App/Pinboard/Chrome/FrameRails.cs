using System.Numerics;
using Microsoft.UI.Composition;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;
using Windows.UI;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// The metal frame: a rail down each side and one along the bottom, no top
/// rail (the board hangs from the screen edge). Bottom up: the bottom rail,
/// its brushed sheen, the side rails over its ends, then the brushed grain of
/// all three (streaks down the sides, across the bottom). Clipped to the
/// board's outline, its bottom corners rounded.
/// </summary>
sealed class FrameRails
{
    const float Rail = (float)BoardMetrics.FrameWidth;
    const float Height = (float)BoardMetrics.ElementHeight;

    /// <summary>A grain tile's length along its rail: the picture at half its pixels.</summary>
    const float GrainLength = 512;

    readonly Compositor _compositor;
    readonly RoundedClip _clip;
    readonly CompositionLinearGradientBrush _bottomBrush;
    readonly CompositionLinearGradientBrush _sheenBrush;
    readonly CompositionLinearGradientBrush _leftBrush;
    readonly CompositionLinearGradientBrush _rightBrush;
    readonly SpriteVisual _bottom;
    readonly SpriteVisual _sheen;
    readonly SpriteVisual _right;
    readonly ContainerVisual _bottomGrain;
    readonly SpriteVisual _rightGrain;
    readonly CompositionSurfaceBrush? _acrossGrain;

    public FrameRails(Compositor compositor)
    {
        _compositor = compositor;
        Visual = compositor.CreateContainerVisual();
        _clip = new RoundedClip(compositor, BoardMetrics.BoardRadius);
        Visual.Clip = _clip.Clip;
        _bottomBrush = Gradients.Down(compositor);
        _sheenBrush = Gradients.Across(compositor);
        _leftBrush = Gradients.Across(compositor);
        // The right rail is the left one turned round: its outer edge on the right.
        _rightBrush = Gradients.Linear(compositor, Vector2.UnitX, Vector2.Zero);
        _bottom = Sprite(_bottomBrush, new Vector2(0, Height - Rail));
        _sheen = Sprite(_sheenBrush, new Vector2(0, Height - Rail));
        _right = Sprite(_rightBrush, Vector2.Zero);
        var left = Sprite(_leftBrush, Vector2.Zero);
        left.Size = _right.Size = new Vector2(Rail, Height);
        _bottomGrain = compositor.CreateContainerVisual();
        _bottomGrain.Offset = new Vector3(0, Height - Rail, 0);
        Visual.Children.InsertAtTop(_bottomGrain);
        if (ChromePictures.Get("Board/chrome/grain-bottom.png") is { } across)
        {
            _acrossGrain = compositor.CreateSurfaceBrush(across);
            _acrossGrain.Stretch = CompositionStretch.Fill;
        }
        var down = ChromePictures.Get("Board/chrome/grain-side.png") is { } picture ? compositor.CreateSurfaceBrush(picture) : null;
        if (down is not null)
        {
            down.Stretch = CompositionStretch.Fill;
        }
        _rightGrain = Sprite(down, Vector2.Zero);
        var leftGrain = Sprite(down, Vector2.Zero);
        leftGrain.Size = _rightGrain.Size = new Vector2(Rail, GrainLength);
    }

    public ContainerVisual Visual { get; }

    public void Paint(FinishTones tones, RailSheen sheen)
    {
        Gradients.SetStops(_bottomBrush, FrameFinishes.BottomStops.Select(s => (s.At / Rail, tones.Bottom[s.Tone].ToColor())));
        var side = FrameFinishes.SideStops.Select(s => (s.At / Rail, tones.Side[s.Tone].ToColor())).ToArray();
        Gradients.SetStops(_leftBrush, side);
        Gradients.SetStops(_rightBrush, side);
        var ends = new Rgba(0, 0, 0, sheen.Ends).ToColor();
        Gradients.SetStops(_sheenBrush,
        [
            (0, ends),
            (RailSheen.BandsAt[0], White(sheen.Bands[0])),
            (RailSheen.GapsAt[0], White(0)),
            (RailSheen.BandsAt[1], White(sheen.Bands[1])),
            (RailSheen.GapsAt[1], White(0)),
            (RailSheen.BandsAt[2], White(sheen.Bands[2])),
            (1, ends),
        ]);
    }

    public void Resize(double width)
    {
        var w = (float)width;
        _clip.Resize(w, Height);
        _bottom.Size = _sheen.Size = new Vector2(w, Rail);
        _right.Offset = _rightGrain.Offset = new Vector3(w - Rail, 0, 0);
        if (_acrossGrain is null)
        {
            return;
        }
        var count = (int)Math.Ceiling(w / GrainLength);
        var tiles = _bottomGrain.Children;
        while (tiles.Count > count)
        {
            tiles.Remove(tiles.Last());
        }
        while (tiles.Count < count)
        {
            var tile = _compositor.CreateSpriteVisual();
            tile.Brush = _acrossGrain;
            tile.Size = new Vector2(GrainLength, Rail);
            tile.Offset = new Vector3(tiles.Count * GrainLength, 0, 0);
            tiles.InsertAtTop(tile);
        }
    }

    static Color White(double alpha) => new Rgba(255, 255, 255, alpha).ToColor();

    SpriteVisual Sprite(CompositionBrush? brush, Vector2 at)
    {
        var sprite = _compositor.CreateSpriteVisual();
        sprite.Brush = brush;
        sprite.Offset = new Vector3(at, 0);
        Visual.Children.InsertAtTop(sprite);
        return sprite;
    }
}
