namespace Tack.Core.Pinboard.Input;

/// <summary>What a wheel step's numbers count.</summary>
public enum WheelMode
{
    /// <summary>Pixels.</summary>
    Pixels,

    /// <summary>Lines of <see cref="ScrollWheel.LinePx"/> each.</summary>
    Lines,

    /// <summary>Views: the row's visible width each.</summary>
    Pages,
}
