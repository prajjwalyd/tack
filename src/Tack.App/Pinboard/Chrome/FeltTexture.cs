using System.Numerics;
using Microsoft.UI.Composition;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// One of the felt's wool pictures, tiled across the felt from its top left:
/// a row of sprites sharing one brush over one decoded picture, as many as
/// the felt's width needs. Each tile is taller than the felt, so one row is enough.
/// </summary>
sealed class FeltTexture
{
    readonly Compositor _compositor;
    readonly CompositionSurfaceBrush? _brush;
    readonly float _size;

    /// <param name="path">The picture, under Assets.</param>
    /// <param name="size">A tile's size, logical px (the picture is drawn at half its pixels, or fully for a soft one).</param>
    public FeltTexture(Compositor compositor, string path, double size)
    {
        (_compositor, _size) = (compositor, (float)size);
        Visual = compositor.CreateContainerVisual();
        if (ChromePictures.Get(path) is { } picture)
        {
            _brush = compositor.CreateSurfaceBrush(picture);
            _brush.Stretch = CompositionStretch.Fill;
        }
    }

    public ContainerVisual Visual { get; }

    /// <summary>Enough tiles for a felt <paramref name="width"/> wide.</summary>
    public void Resize(double width)
    {
        if (_brush is null)
        {
            return;
        }
        var count = Math.Max(1, (int)Math.Ceiling(width / _size));
        var tiles = Visual.Children;
        while (tiles.Count > count)
        {
            tiles.Remove(tiles.Last());
        }
        while (tiles.Count < count)
        {
            var tile = _compositor.CreateSpriteVisual();
            tile.Brush = _brush;
            tile.Size = new Vector2(_size);
            tile.Offset = new Vector3(tiles.Count * _size, 0, 0);
            tiles.InsertAtTop(tile);
        }
    }
}
