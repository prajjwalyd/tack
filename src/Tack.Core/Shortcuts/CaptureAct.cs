namespace Tack.Core.Shortcuts;

/// <summary>What a key press means to a shortcut field that is listening.</summary>
public enum CaptureAct
{
    /// <summary>Tab: the focus moves on and the field stops listening.</summary>
    Leave,

    /// <summary>A modifier on its own: show what is held.</summary>
    Held,

    /// <summary>Esc: stop listening, nothing changes.</summary>
    Cancel,

    /// <summary>Backspace or Delete: turn the shortcut off.</summary>
    Off,

    /// <summary>Not a usable chord: say how to make one.</summary>
    Hint,

    /// <summary>A chord to apply.</summary>
    Chord,
}
