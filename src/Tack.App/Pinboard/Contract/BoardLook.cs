using Tack.Core.Appearance;
using Tack.Core.Reveal;
using Tack.Core.Settings;

namespace Tack.App.Pinboard.Contract;

/// <summary>The settings the board follows: sound, the felt, the frame, the note paper and the speed.</summary>
sealed record BoardLook(bool Sound, Felt Felt, Frame Frame, NotePaper Paper, RevealSpeed Speed)
{
    /// <summary>What every animation on the board is multiplied by: 1 for Slow.</summary>
    public double MotionScale => Speed.MotionScale();

    public static BoardLook Of(TackSettings settings) => new(
        settings.Sound, settings.Appearance.Felt, settings.Appearance.Frame, settings.Appearance.Paper, settings.Reveal);
}
