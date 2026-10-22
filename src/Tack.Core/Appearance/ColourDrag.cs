namespace Tack.Core.Appearance;

/// <summary>
/// A colour dragged about in the colour picker. Sending each one would save
/// the settings dozens of times a second, so a move waits a short
/// <see cref="Beat"/> and then only the latest is sent; when the picker
/// closes, a colour still waiting is sent at once. The caller keeps the timer.
/// </summary>
public sealed class ColourDrag
{
    /// <summary>How long a moved colour waits to be sent: at most one a beat while dragging.</summary>
    public static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(120);

    Felt.CustomFelt? _sent;
    Felt.CustomFelt? _waiting;

    /// <summary>The picker opens on <paramref name="start"/>: anything waiting from before is dropped.</summary>
    public void Open(Felt.CustomFelt start) => (_sent, _waiting) = (start, null);

    /// <summary>The picker moved to <paramref name="colour"/>. True when a beat should start: nothing was waiting yet.</summary>
    public bool Move(Felt.CustomFelt colour)
    {
        if (colour == (_waiting ?? _sent))
        {
            return false;
        }
        var first = _waiting is null;
        _waiting = colour;
        return first;
    }

    /// <summary>The beat is over: the colour to send now, if one is waiting.</summary>
    public Felt.CustomFelt? Due()
    {
        var due = _waiting;
        _waiting = null;
        if (due is not null)
        {
            _sent = due;
        }
        return due;
    }

    /// <summary>The picker closed: the colour still to send, if one is waiting.</summary>
    public Felt.CustomFelt? Close() => Due();
}
