using Tack.Core.Phone;
using Tack.Core.Reveal;
using Tack.Core.Shortcuts;

namespace Tack.Core.Settings;

/// <summary>The user's settings, changed in Settings or from the tray, and saved in board.json.</summary>
public sealed record TackSettings
{
    /// <summary>The pin and pop sounds.</summary>
    public bool Sound { get; init; } = true;

    /// <summary>Resting the pointer against the top edge reveals the board.</summary>
    public bool EdgeReveal { get; init; } = true;

    /// <summary>How soon resting at the top edge reveals the board, and how fast it moves.</summary>
    public RevealSpeed Reveal { get; init; } = RevealSpeed.Normal;

    public AppearanceSettings Appearance { get; init; } = new();

    public UpdateSettings Updates { get; init; } = new();

    /// <summary>The one-time tip about Snipping Tool's own notification has been shown.</summary>
    public bool SnipTipShown { get; init; }

    /// <summary>Shows or hides the board, as "Win+Alt+S"; empty for none.</summary>
    public string ToggleShortcut { get; init; } = Chord.DefaultToggle;

    /// <summary>Pins the current selection, as "Win+Alt+C"; empty for none.</summary>
    public string PinShortcut { get; init; } = Chord.DefaultPin;

    /// <summary>The board on the user's phone.</summary>
    public PhoneSettings Phone { get; init; } = new();

    /// <summary>The toggle shortcut; one that no longer parses falls back to the default, and empty is off.</summary>
    public Chord? ToggleChord() => ChordOr(ToggleShortcut, Chord.DefaultToggle);

    /// <summary>The pin-selection shortcut, with the same fallback.</summary>
    public Chord? PinChord() => ChordOr(PinShortcut, Chord.DefaultPin);

    static Chord? ChordOr(string text, string fallback)
    {
        if (Chord.TryParse(text, out var chord, out _))
        {
            return chord;
        }
        Chord.TryParse(fallback, out chord, out _);
        return chord;
    }
}
