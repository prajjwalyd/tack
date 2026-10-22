namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// The values one keyframe sets. A channel left null has no keyframe here.
/// The transform is one value: a pose that sets any transform channel (or is
/// <see cref="None"/>) puts every other transform channel the motion uses at
/// rest, as a transform list padded with identities does. Opacity stands alone.
/// </summary>
public readonly record struct Pose
{
    /// <summary>The transform at rest (no translation or rotation, scale 1); opacity left alone.</summary>
    public static Pose None { get; } = new() { Identity = true };

    public double? Opacity { get; init; }

    public double? X { get; init; }

    public double? Y { get; init; }

    public double? Scale { get; init; }

    public double? ScaleX { get; init; }

    public double? ScaleY { get; init; }

    public double? Rotation { get; init; }

    /// <summary>The transform is at rest, apart from the transform channels set here.</summary>
    public bool Identity { get; init; }

    /// <summary>Whether this pose sets the transform.</summary>
    public bool HasTransform => Identity || X.HasValue || Y.HasValue || Scale.HasValue || ScaleX.HasValue
        || ScaleY.HasValue || Rotation.HasValue;

    /// <summary>A channel at rest: opacity and scales 1, the rest 0.</summary>
    public static double Rest(Channel channel) =>
        channel is Channel.Opacity or Channel.Scale or Channel.ScaleX or Channel.ScaleY ? 1 : 0;

    public static bool IsTransform(Channel channel) => channel != Channel.Opacity;

    /// <summary>The value set for <paramref name="channel"/>, if any; a transform channel follows <see cref="HasTransform"/>.</summary>
    public double? ValueOf(Channel channel)
    {
        var set = Explicit(channel);
        if (set.HasValue || !IsTransform(channel) || !HasTransform)
        {
            return set;
        }
        return Rest(channel);
    }

    /// <summary>Only what this pose names itself.</summary>
    public double? Explicit(Channel channel) => channel switch
    {
        Channel.Opacity => Opacity,
        Channel.X => X,
        Channel.Y => Y,
        Channel.Scale => Scale,
        Channel.ScaleX => ScaleX,
        Channel.ScaleY => ScaleY,
        _ => Rotation,
    };
}
