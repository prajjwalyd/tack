namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// A damped spring's step response, sampled at 41 evenly spaced moments and
/// played straight between them, with how long it takes to settle. The
/// board's reveal swing, the row's slides and the hover settle are springs.
/// </summary>
public sealed class Spring
{
    /// <summary>Samples are taken at 0, 1/40 ... 1 of the duration.</summary>
    public const int Steps = 40;

    readonly double[] _samples;

    /// <param name="response">The undamped period, seconds.</param>
    /// <param name="damping">0 to 1: lower overshoots more.</param>
    /// <param name="settle">The fraction of the travel left when the spring may end.</param>
    /// <param name="v0">The starting speed, travels per second.</param>
    public Spring(double response, double damping, double settle = 0.003, double v0 = 0)
    {
        var w0 = 2 * Math.PI / response;
        var z = Math.Min(damping, 0.999);
        var wd = w0 * Math.Sqrt(1 - z * z);
        var duration = Math.Log(1 / settle) / (z * w0);
        double X(double t) =>
            1 - Math.Exp(-z * w0 * t) * (Math.Cos(wd * t) + (z * w0 - v0) / wd * Math.Sin(wd * t));
        _samples = new double[Steps + 1];
        for (var i = 0; i <= Steps; i++)
        {
            // Four decimals, as the samples were always written; the last is exactly 1.
            _samples[i] = i == Steps ? 1 : Math.Round(X(i / (double)Steps * duration), 4, MidpointRounding.AwayFromZero);
        }
        Ms = (int)Math.Round(duration * 1000, MidpointRounding.AwayFromZero);
        Easing = new SampledEasing(_samples);
    }

    /// <summary>The board swinging down on its hinge, overshooting flat by a few degrees: 477 ms.</summary>
    public static Spring Reveal { get; } = new(0.42, 0.62, 0.012, 1.5);

    /// <summary>The row's slides (gaps closing, reorders), with no visible overshoot: 452 ms.</summary>
    public static Spring Layout { get; } = new(0.44, 0.9);

    /// <summary>A print settling after hover, with a whisper of bounce: 314 ms.</summary>
    public static Spring Settle { get; } = new(0.3, 0.7, 0.01);

    /// <summary>The 41 samples, from 0 to exactly 1; they may pass 1 on the way.</summary>
    public IReadOnlyList<double> Samples => _samples;

    /// <summary>How long it takes at Slow, whole ms.</summary>
    public int Ms { get; }

    /// <summary>The samples as an easing, straight between them.</summary>
    public SampledEasing Easing { get; }
}
