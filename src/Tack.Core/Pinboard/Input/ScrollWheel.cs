namespace Tack.Core.Pinboard.Input;

/// <summary>
/// Reads a wheel or touchpad step. The larger axis wins, so a vertical wheel
/// scrolls the row sideways. A wheel steps in large round notches, which
/// glide and stack; a touchpad sends many small steps, followed almost directly.
/// </summary>
public static class ScrollWheel
{
    /// <summary>A line, for a step counted in lines.</summary>
    public const double LinePx = 40;

    /// <summary>The smallest pixel step that can be a notch.</summary>
    const double NotchPx = 50;

    /// <summary>A notch's share of Windows' wheel delta.</summary>
    const int Detent = 120;

    /// <summary>A Windows notch with the wheel setting at its default of three lines, px, as 0.2's board was given it.</summary>
    const double WindowsNotchPx = 100;

    const int WindowsLines = 3;

    /// <summary>
    /// A step of <paramref name="dx"/> and <paramref name="dy"/> in
    /// <paramref name="mode"/> units. A notch is a step in lines or pages, or a
    /// whole number of pixels, at least 50, with no sideways part.
    /// </summary>
    public static WheelTurn Read(double dx, double dy, WheelMode mode, double pageWidth)
    {
        var unit = mode switch
        {
            WheelMode.Lines => LinePx,
            WheelMode.Pages => pageWidth,
            _ => 1,
        };
        (dx, dy) = (dx * unit, dy * unit);
        var d = Math.Abs(dx) > Math.Abs(dy) ? dx : dy;
        var notch = mode != WheelMode.Pixels || (Math.Abs(d) >= NotchPx && double.IsInteger(d) && dx == 0);
        return new WheelTurn(d, notch);
    }

    /// <summary>
    /// A step from one Windows wheel message: <paramref name="delta"/> in
    /// 120ths of a notch (positive is forward or, <paramref name="horizontal"/>,
    /// right), with the wheel setting's <paramref name="lines"/> per notch
    /// (negative for a page per notch). A notch is a whole detent of a mouse's
    /// vertical wheel, or any page step; a touchpad's step, a fine step or a
    /// sideways one never is.
    /// </summary>
    public static WheelTurn FromWindows(int delta, bool horizontal, int lines, bool touchpad, double pageWidth)
    {
        var page = lines < 0;
        // Multiplied out before the one division, so a whole notch stays a whole number of px.
        var amount = page ? (double)delta / Detent : delta * (double)lines * WindowsNotchPx / (Detent * WindowsLines);
        var mode = page ? WheelMode.Pages : WheelMode.Pixels;
        // A forward turn goes back towards the row's start.
        var turn = horizontal ? Read(amount, 0, mode, pageWidth) : Read(0, -amount, mode, pageWidth);
        var detent = delta % Detent == 0 && !horizontal;
        return turn with { Notch = delta != 0 && !touchpad && (page || detent) };
    }
}
