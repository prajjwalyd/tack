namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A cubic Bézier easing from (0, 0) to (1, 1) through the control points
/// (x1, y1) and (x2, y2), solved for x by 24 steps of bisection, as Tack 0.2's
/// flight did. Outside 0 to 1 it holds at the ends.
/// </summary>
public sealed class CubicBezier(double x1, double y1, double x2, double y2) : Easing
{
    const int Steps = 24;

    /// <summary>The standard "ease".</summary>
    public static CubicBezier Ease { get; } = new(0.25, 0.1, 0.25, 1);

    public static CubicBezier EaseIn { get; } = new(0.42, 0, 1, 1);

    public static CubicBezier EaseOut { get; } = new(0, 0, 0.58, 1);

    public static CubicBezier EaseInOut { get; } = new(0.42, 0, 0.58, 1);

    /// <summary>The board folding away, gathering speed.</summary>
    public static CubicBezier Tuck { get; } = new(0.5, 0, 0.85, 0.35);

    /// <summary>The board's own quick ease-out (hover lift, notices).</summary>
    public static CubicBezier QuickOut { get; } = new(0.2, 0.8, 0.25, 1);

    public (double X1, double Y1, double X2, double Y2) Points => (x1, y1, x2, y2);

    public override double At(double x)
    {
        if (x <= 0)
        {
            return 0;
        }
        if (x >= 1)
        {
            return 1;
        }
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < Steps; i++)
        {
            t = (lo + hi) / 2;
            if (Curve(x1, x2, t) < x)
            {
                lo = t;
            }
            else
            {
                hi = t;
            }
        }
        return Curve(y1, y2, t);
    }

    static double Curve(double p1, double p2, double t) =>
        3 * p1 * t * Math.Pow(1 - t, 2) + 3 * p2 * t * t * (1 - t) + Math.Pow(t, 3);
}
