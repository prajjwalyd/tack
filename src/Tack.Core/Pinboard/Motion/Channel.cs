namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// What a motion moves on a layer. The transform channels are one transform,
/// applied about the layer's centre point as translate, rotate, then scale.
/// </summary>
public enum Channel
{
    Opacity,

    /// <summary>Translation, logical px.</summary>
    X,

    Y,

    /// <summary>Uniform scale; the layer's own scale on both axes.</summary>
    Scale,

    ScaleX,

    ScaleY,

    /// <summary>Degrees about the layer's rotation axis (the screen's normal unless the layer says otherwise).</summary>
    Rotation,
}
