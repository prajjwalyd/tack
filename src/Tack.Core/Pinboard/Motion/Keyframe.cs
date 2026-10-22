namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// One keyframe of a <see cref="Timeline"/>: where it sits (null to be spread
/// evenly between its neighbours), what it sets, and the easing on the way to
/// the next keyframe (linear if null).
/// </summary>
public sealed record Keyframe(double? Offset, Pose Pose, Easing? Easing);
