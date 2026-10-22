namespace Tack.Core.Shortcuts;

/// <summary>
/// A new pair of shortcuts, checked before anything is registered: both must
/// parse, and two that are on cannot share a chord (the second counts as taken,
/// by Tack itself). <see cref="Answer"/> holds the tidied texts with
/// <see cref="ShortcutStatus.Ok"/> or <see cref="ShortcutStatus.Off"/> until
/// registering says otherwise, or the reason the pair is refused.
/// </summary>
/// <param name="Toggle">The show-or-hide chord to register; null for off.</param>
/// <param name="Pin">The pin-selection chord to register; null for off.</param>
public sealed record ShortcutPlan(ShortcutPair Answer, Chord? Toggle, Chord? Pin)
{
    /// <summary>Both are usable and distinct, so they may be registered.</summary>
    public bool CanTry => Answer.AllWork;

    /// <summary>Checks <paramref name="toggle"/> and <paramref name="pin"/>, as "Win+Alt+S"; empty turns one off.</summary>
    public static ShortcutPlan For(string toggle, string pin)
    {
        var toggleOk = Chord.TryParse(toggle, out var toggleChord, out _);
        var pinOk = Chord.TryParse(pin, out var pinChord, out _);
        var answer = new ShortcutPair(State(toggle, toggleOk, toggleChord), State(pin, pinOk, pinChord));
        if (toggleOk && pinOk && toggleChord is not null && toggleChord == pinChord)
        {
            answer = answer with { Pin = answer.Pin with { Status = ShortcutStatus.InUse } };
        }
        return new ShortcutPlan(answer, toggleChord, pinChord);
    }

    static ShortcutState State(string text, bool parsed, Chord? chord) => new(
        Chord.Tidy(text),
        !parsed ? ShortcutStatus.Invalid : chord is null ? ShortcutStatus.Off : ShortcutStatus.Ok);
}
