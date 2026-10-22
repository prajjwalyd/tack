namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// How a motion's progress becomes its value's progress: 0 goes to 0 and 1 to
/// 1, and between them the value may run ahead, lag or overshoot.
/// </summary>
public abstract class Easing
{
    /// <summary>Progress as it is.</summary>
    public static Easing Linear { get; } = new SampledEasing([0.0, 1.0]);

    /// <summary>The eased progress at <paramref name="x"/>, 0 to 1.</summary>
    public abstract double At(double x);

    /// <summary>
    /// Where the curve has corners, 0 to 1: sampling must include them to
    /// reproduce it exactly. Empty for a smooth curve.
    /// </summary>
    public virtual IReadOnlyList<double> Knots => [];
}
