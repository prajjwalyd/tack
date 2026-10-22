namespace Tack.Core.Snips;

/// <summary>Where a candidate rectangle comes from.</summary>
public enum Source
{
    /// <summary>A corner of a dragged selection, around the pointer.</summary>
    Corner,

    /// <summary>The window under the pointer: a window snip.</summary>
    Window,

    /// <summary>The whole monitor: a full-screen snip.</summary>
    Monitor,
}
