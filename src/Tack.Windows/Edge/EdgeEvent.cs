namespace Tack.Windows.Edge;

/// <summary>What the edge strips saw.</summary>
internal enum EdgeEvent
{
    /// <summary>The pointer reached a zone, and is there until <see cref="Left"/>.</summary>
    Arrived,

    Left,

    /// <summary>Something carried with a button held came over a zone, until <see cref="DragLeft"/>.</summary>
    DragEntered,

    DragLeft,
}
