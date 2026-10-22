namespace Tack.Windows.Shell;

/// <summary>What a drop target did with a dragged file.</summary>
[Flags]
public enum DropEffect
{
    /// <summary>Nothing took it, or the drag was cancelled.</summary>
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
}
