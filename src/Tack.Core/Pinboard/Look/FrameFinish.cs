namespace Tack.Core.Pinboard.Look;

/// <summary>
/// A frame finish: a hand-tuned set of tones for each theme, dimmer in the
/// dark one so it never glares.
/// </summary>
/// <param name="Id">The finish's id, as the Frame setting saves it.</param>
public sealed record FrameFinish(string Id, FinishTones Light, FinishTones Dark)
{
    /// <summary>The tones for the light or dark theme.</summary>
    public FinishTones In(bool dark) => dark ? Dark : Light;
}
