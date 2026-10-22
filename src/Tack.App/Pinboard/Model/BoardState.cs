namespace Tack.App.Pinboard.Model;

/// <summary>
/// What the board knows and is doing, shared by its parts. UI thread only,
/// so no lock: the app's state is never read from here.
/// </summary>
sealed class BoardState
{
    /// <summary>Down, or coming down. Cleared the moment a tuck starts.</summary>
    public bool Revealed { get; set; }

    /// <summary>Windows asks for less motion: plain fades and slides instead of swings.</summary>
    public bool Reduced { get; set; }

    /// <summary>Every print the board holds, leaving ones included, by id.</summary>
    public Dictionary<string, BoardItem> Items { get; } = [];

    /// <summary>The row's ids, in order: kept prints first, then the rest newest first. Leaving prints are not in it.</summary>
    public List<string> Order { get; } = [];

    /// <summary>The print with the hover look, if any: one at a time.</summary>
    public BoardItem? Hovered { get; set; }

    /// <summary>The print a press is on, until the press ends.</summary>
    public string? Pressing { get; set; }

    /// <summary>The print being dragged out, from its dragging event to its drag-ended one.</summary>
    public string? DraggingId { get; set; }

    /// <summary>Prints that arrived while tucked, to pin on at the next reveal.</summary>
    public List<BoardItem> Awaiting { get; } = [];

    /// <summary>The board's width, logical px: the row's visible width.</summary>
    public double BoardWidth { get; set; }

    /// <summary>How far the row is scrolled, logical px from its start.</summary>
    public double ScrollPosition { get; set; }

    /// <summary>The window's size, logical px; 0 until it is first laid out.</summary>
    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    /// <summary>Physical px per logical px on the window's monitor.</summary>
    public double Scale { get; set; } = 1;

    public BoardItem? Find(string id) => Items.GetValueOrDefault(id);

    /// <summary>The row's prints, in order.</summary>
    public IEnumerable<BoardItem> Row => Order.Select(Find).OfType<BoardItem>();

    /// <summary>How many prints at the row's start are kept: where a new unkept print goes.</summary>
    public int KeptCount => Row.Count(item => item.Print.Kept);
}
