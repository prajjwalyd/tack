namespace Tack.Windows.Edge;

/// <summary>What a look at the pointer at the top edge makes of it.</summary>
internal enum Rest
{
    No,

    /// <summary>Rested for the dwell: reveal.</summary>
    Edge,

    /// <summary>Held over the edge for the drag's dwell: reveal for the drop.</summary>
    Drag,
}
