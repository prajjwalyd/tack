using Tack.Core.Reveal;

namespace Tack.Core.Pinboard;

/// <summary>
/// The board's fixed sizes and times, in logical px and in ms at Slow (motion
/// scale 1). Every part of the board reads them from here, so the window, the
/// row and the motions agree.
/// </summary>
public static class BoardMetrics
{
    /// <summary>The part of the board above the screen edge; the hinge is at the edge.</summary>
    public const double TopHidden = 24;

    /// <summary>The visible board, from the screen edge down.</summary>
    public const double BoardHeight = 150;

    /// <summary>The board's element: the hidden part and the visible part, hanging at y = -<see cref="TopHidden"/>.</summary>
    public const double ElementHeight = TopHidden + BoardHeight;

    /// <summary>The bottom corners, outside the frame; the top has none.</summary>
    public const double BoardRadius = 12;

    /// <summary>The metal frame on the sides and the bottom; there is no top rail.</summary>
    public const double FrameWidth = 6;

    /// <summary>The bottom corner caps.</summary>
    public const double CapSize = 14;

    /// <summary>How far the row's rail runs below the board, so a falling print stays in view.</summary>
    public const double RailOverhang = 210;

    /// <summary>The rail, from the screen edge down into the overhang.</summary>
    public const double RailHeight = BoardHeight + RailOverhang;

    /// <summary>How far away the eye is from the hinge.</summary>
    public const double Perspective = 1100;

    /// <summary>The board's angle while tucked: folded up under the screen edge, degrees about the hinge.</summary>
    public const double HingeFrom = -88;

    /// <summary>Margin around the board's rect where the window still takes the pointer, for a hovered print's lift.</summary>
    public const double Lift = 8;

    /// <summary>The tuck's fold, the same length as the app's.</summary>
    public const double TuckMs = RevealSpeedTimes.TuckMs;

    /// <summary>After the fold, a margin of frames before the board lets go of everything. Class C.</summary>
    public const double TuckCleanupMarginMs = 30;

    /// <summary>After the reveal swing, a couple of frames before the board counts as at rest. Class C.</summary>
    public const double RestMarginMs = 40;

    /// <summary>Until the board is at rest under reduced motion, where it slides plainly.</summary>
    public const double ReducedRestMs = 100;

    /// <summary>The reduced-motion slide down (ease-out) and up (ease-in).</summary>
    public const double ReducedRevealMs = 160;

    public const double ReducedTuckMs = 140;

    /// <summary>A burst of layouts sends one rect. Class C.</summary>
    public const double RectCoalesceMs = 16;

    /// <summary>A burst of window size changes lays out once. Class C.</summary>
    public const double ResizeDebounceMs = 60;

    /// <summary>Between neighbouring prints, and between the last kept print and the first that is not.</summary>
    public const double Gap = 18;

    public const double GroupGap = 40;

    /// <summary>Felt left and right of the outermost prints.</summary>
    public const double Pad = 24;

    /// <summary>The narrowest board; the widest is the window less <see cref="Side"/>.</summary>
    public const double MinWidth = 360;

    public const double Side = 80;

    /// <summary>Prints hang from this line, below the screen edge.</summary>
    public const double PrintTop = 13;

    /// <summary>The pin's centre, down from a print's top: every print layer turns about (50%, this).</summary>
    public const double PinY = 7;

    /// <summary>A photo's box: the long side at most 160, at most 100 tall, never smaller than 40 by 30.</summary>
    public const double PhotoMaxWidth = 160;

    public const double PhotoMaxHeight = 100;

    public const double PhotoMin = 40;

    public const double PhotoMinHeight = PhotoMin * 0.75;

    /// <summary>A picture whose size is not known shows 16:9 at the widest.</summary>
    public const double UnknownWidth = PhotoMaxWidth;

    public const double UnknownHeight = 90;

    /// <summary>The even paper border around a photo.</summary>
    public const double PrintBorder = 4;

    /// <summary>A paper note is one fixed size.</summary>
    public const double NoteWidth = 136;

    public const double NoteHeight = 86;

    /// <summary>Each print's resting tilt, either way, degrees.</summary>
    public const double MinTilt = 0.3;

    public const double MaxTilt = 1.5;

    /// <summary>The pin head: 10.5 px drawn at 1.3, centred at (w / 2, <see cref="PinY"/>).</summary>
    public const double PinScale = 1.3;

    public const double PinSize = 10.5 * PinScale;

    /// <summary>A kept print's brass pin is this much larger.</summary>
    public const double KeptPinScale = 1.15;

    /// <summary>The paper's corners, and the photo's inside it.</summary>
    public const double PrintRadius = 3;

    public const double PhotoRadius = 1;
}
