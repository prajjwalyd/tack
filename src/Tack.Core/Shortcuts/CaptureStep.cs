namespace Tack.Core.Shortcuts;

/// <summary>
/// What a shortcut field does with a key press, and the keys to show on its
/// keycaps: the modifiers held for <see cref="CaptureAct.Held"/>, the whole
/// chord for <see cref="CaptureAct.Chord"/>, none otherwise.
/// </summary>
public sealed record CaptureStep(CaptureAct Act, IReadOnlyList<string> Keys)
{
    /// <summary>The chord as text, "Win+Alt+S"; empty unless <see cref="Act"/> is a chord.</summary>
    public string Chord => Act == CaptureAct.Chord ? string.Join('+', Keys) : string.Empty;
}
