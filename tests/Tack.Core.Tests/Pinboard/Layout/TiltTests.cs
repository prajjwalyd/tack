using Tack.Core.Pinboard.Layout;

namespace Tack.Core.Tests.Pinboard.Layout;

public class TiltTests
{
    /// <summary>Hands out the given numbers in turn, as <see cref="Random.NextDouble"/> would.</summary>
    sealed class Script(params double[] values) : Random
    {
        int _next;

        public override double NextDouble() => values[_next++];
    }

    [Theory]
    [InlineData(0.2, 0.0, -0.3)]
    [InlineData(0.2, 1.0, -1.5)]
    [InlineData(0.5, 0.5, 0.9)]
    [InlineData(0.9, 0.25, 0.6)]
    public void A_print_hangs_between_0_3_and_1_5_degrees_either_way(double side, double amount, double expected)
    {
        Assert.Equal(expected, Tilt.Next(new Script(side, amount)), 9);
    }

    [Theory]
    [InlineData(1.2, 0.0, 0.8)]
    [InlineData(1.2, 1.0, 1.6)]
    [InlineData(-0.4, 0.5, -1.2)]
    [InlineData(0.0, 0.25, 1.0)]
    public void A_pin_on_leans_the_way_the_print_tilts(double tilt, double amount, double expected)
    {
        Assert.Equal(expected, Tilt.Lean(tilt, new Script(amount)), 9);
    }

    [Fact]
    public void Tilts_stay_in_range_with_real_randomness()
    {
        var random = new Random(7);
        for (var i = 0; i < 1000; i++)
        {
            var tilt = Math.Abs(Tilt.Next(random));
            Assert.InRange(tilt, 0.3, 1.5);
        }
    }
}
