using System.Numerics;
using Microsoft.UI.Composition;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// A clip to a rect with its bottom corners rounded. The top corners are
/// rounded too but sit out of sight above the visual, which is never seen
/// above the screen edge.
/// </summary>
sealed class RoundedClip
{
    readonly CompositionRoundedRectangleGeometry _shape;
    readonly float _radius;

    public RoundedClip(Compositor compositor, double radius)
    {
        _radius = (float)radius;
        _shape = compositor.CreateRoundedRectangleGeometry();
        _shape.CornerRadius = new Vector2(_radius);
        Clip = compositor.CreateGeometricClip(_shape);
    }

    public CompositionGeometricClip Clip { get; }

    /// <summary>Clips to (0, 0) to (<paramref name="width"/>, <paramref name="height"/>) in the clipped visual's space.</summary>
    public void Resize(double width, double height)
    {
        _shape.Offset = new Vector2(0, -_radius);
        _shape.Size = new Vector2((float)width, (float)height + _radius);
    }
}
