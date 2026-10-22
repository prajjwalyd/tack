namespace Tack.Core.Phone;

/// <summary>
/// The phone board as board.json alone tells it (<see cref="PhoneState.Saved"/>):
/// nothing watches NetBird or serves the board, so nothing changes by itself
/// and nobody asks. What Settings uses until the phone server exists.
/// </summary>
public sealed class SavedPhoneLink : IPhoneLink
{
    public static SavedPhoneLink Instance { get; } = new();

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public PhoneState State(PhoneSettings saved) => PhoneState.Saved(saved);

    /// <summary>Nobody is asking, so there is nothing to answer.</summary>
    public bool Answer(string key, bool allow) => false;
}
