using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>Golden values from Tack 0.2's own bisection, run as it shipped, at x = 0, .1, .25, .5, .75, .9 and 1.</summary>
public class CubicBezierTests
{
    static readonly double[] Xs = [0, 0.1, 0.25, 0.5, 0.75, 0.9, 1];

    public static TheoryData<string, double[]> Curves => new()
    {
        { "tuck", [0, 0.004849023175839554, 0.032310034058446103, 0.147326557689944, 0.3986463023366763, 0.6788155341766097, 1] },
        { "travel", [0, 0.015828754169327768, 0.13831800702783884, 0.7095838691818246, 0.9517725757269635, 0.9934858677254178, 1] },
        { "ease", [0, 0.09479633400352007, 0.4085106117304686, 0.802403324431417, 0.9604589534895495, 0.9943164915399348, 1] },
        { "ease-in", [0, 0.01702659105214267, 0.09346469571463167, 0.3153568354605525, 0.621861878840659, 0.8394278757249442, 1] },
        { "ease-out", [0, 0.16057212427505588, 0.378138121159341, 0.6846431645394475, 0.9065353042853683, 0.9829734089478573, 1] },
        { "ease-in-out", [0, 0.01972243912626354, 0.12916194291816122, 0.49999991059303284, 0.8708380570818388, 0.9802775608737365, 1] },
        { "overshoot", [0, 0.41442422246163063, 0.8261935155006783, 1.0488281052028547, 1.027279471549769, 1.0050541403858424, 1] },
    };

    static CubicBezier Named(string name) => name switch
    {
        "tuck" => CubicBezier.Tuck,
        "travel" => new CubicBezier(0.5, 0, 0.25, 1),
        "ease" => CubicBezier.Ease,
        "ease-in" => CubicBezier.EaseIn,
        "ease-out" => CubicBezier.EaseOut,
        "ease-in-out" => CubicBezier.EaseInOut,
        _ => new CubicBezier(0.3, 1.4, 0.5, 1),
    };

    [Theory]
    [MemberData(nameof(Curves))]
    public void Matches_the_old_bisection(string name, double[] expected)
    {
        var curve = Named(name);
        for (var i = 0; i < Xs.Length; i++)
        {
            Assert.Equal(expected[i], curve.At(Xs[i]), 15);
        }
    }

    [Fact]
    public void Outside_0_to_1_it_holds_at_the_ends()
    {
        var curve = new CubicBezier(0.3, 1.4, 0.5, 1);
        Assert.Equal(0, curve.At(-0.5));
        Assert.Equal(1, curve.At(1.5));
    }

    [Fact]
    public void The_keywords_are_the_standard_curves()
    {
        Assert.Equal((0.25, 0.1, 0.25, 1.0), CubicBezier.Ease.Points);
        Assert.Equal((0.42, 0.0, 1.0, 1.0), CubicBezier.EaseIn.Points);
        Assert.Equal((0.0, 0.0, 0.58, 1.0), CubicBezier.EaseOut.Points);
        Assert.Equal((0.42, 0.0, 0.58, 1.0), CubicBezier.EaseInOut.Points);
        Assert.Equal((0.5, 0.0, 0.85, 0.35), CubicBezier.Tuck.Points);
        Assert.Equal(0.37, Easing.Linear.At(0.37), 15);
    }
}
