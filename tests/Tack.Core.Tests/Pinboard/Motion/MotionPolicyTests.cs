using Tack.Core.Pinboard.Motion;

namespace Tack.Core.Tests.Pinboard.Motion;

/// <summary>
/// The Speed setting moves the whole board uniformly: each print motion's
/// time is what its class says at every speed, as the motion's own code
/// works it out, and a time that does not follow the speed says why.
/// </summary>
public class MotionPolicyTests
{
    static readonly double[] Speeds = [1, 0.65, 0.35];

    [Fact]
    public void Every_time_is_what_its_class_says_at_every_speed()
    {
        foreach (var scale in Speeds)
        {
            var clock = new MotionClock();
            clock.SetScale(scale);
            foreach (var timing in MotionPolicy.All)
            {
                var expected = MotionPolicy.Expected(timing, clock);
                Assert.True(Math.Abs(timing.At(clock) - expected) < 1e-9, $"{timing.Name} at {scale}: {timing.At(clock)}, not {expected}");
            }
        }
    }

    [Fact]
    public void A_time_that_does_not_follow_the_speed_says_why_and_only_then()
    {
        foreach (var timing in MotionPolicy.All)
        {
            var fixedTime = timing.Class is MotionClass.C or MotionClass.D;
            Assert.True(fixedTime == !string.IsNullOrWhiteSpace(timing.Why), timing.Name);
            Assert.True((timing.Class == MotionClass.B) == timing.Floor.HasValue, timing.Name);
        }
    }

    [Fact]
    public void The_classes_are_pinned()
    {
        var classes = MotionPolicy.All.ToDictionary(t => t.Name, t => t.Class);
        Assert.Equal(MotionPolicy.All.Count, classes.Count);
        Assert.Equal(MotionClass.B, classes["badge hold"]);
        Assert.Equal(MotionClass.C, classes["breeze interval"]);
        Assert.Equal(MotionClass.C, classes["flight decode wait"]);
        Assert.Equal(3, MotionPolicy.All.Count(t => t.Class != MotionClass.A));
        Assert.Equal(43, MotionPolicy.All.Count);
    }

    [Fact]
    public void The_badge_holds_a_readable_while_at_any_speed()
    {
        var instant = new MotionClock();
        instant.SetScale(0.35);
        Assert.Equal(600, BoardMotions.BadgeHold(instant));
        Assert.Equal(1000, BoardMotions.BadgeHold(new MotionClock()));
    }
}
