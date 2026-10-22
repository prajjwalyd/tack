using System.Text.Json;
using Tack.Core.Reveal;
using Tack.Core.Storage.Json;
using Tack.Core.Tests.Storage;

namespace Tack.Core.Tests.Reveal;

public class RevealSpeedTests
{
    static readonly RevealSpeed[] All = [RevealSpeed.Instant, RevealSpeed.Normal, RevealSpeed.Slow];

    static TimeSpan Ms(int n) => TimeSpan.FromMilliseconds(n);

    [Fact]
    public void Normal_is_the_default_and_slow_keeps_the_original_pace()
    {
        Assert.Equal(RevealSpeed.Normal, default(RevealSpeed));
        Assert.Equal(Ms(400), RevealSpeed.Slow.Dwell());
        Assert.Equal(1.0, RevealSpeed.Slow.MotionScale());
    }

    [Fact]
    public void Slow_waits_exactly_as_it_always_did()
    {
        var slow = RevealSpeed.Slow;
        Assert.Equal(Ms(3000), slow.Peek());
        Assert.Equal(Ms(1750), slow.FlightPeek());
        Assert.Equal(Ms(1600), slow.NoticePeek());
        Assert.Equal(Ms(250), slow.LeaveDelay());
        Assert.Equal(Ms(300), slow.TuckHideDelay());
        Assert.Equal(Ms(1234), slow.Scaled(Ms(1234)));
        Assert.Equal(Ms(1234), slow.ScaledMin(Ms(1234), Ms(5000)));
    }

    [Fact]
    public void Quicker_speeds_wait_less_and_move_quicker()
    {
        Assert.Equal(Ms(40), RevealSpeed.Instant.Dwell());
        Assert.Equal(Ms(150), RevealSpeed.Normal.Dwell());
        for (var i = 0; i < All.Length - 1; i++)
        {
            Assert.True(All[i].Dwell() < All[i + 1].Dwell());
            Assert.True(All[i].MotionScale() < All[i + 1].MotionScale());
        }
        // The presets are clearly different paces, not nearly the same.
        var (instant, normal, slow) = (All[0].MotionScale(), All[1].MotionScale(), All[2].MotionScale());
        Assert.True(normal / instant > 1.5 && slow / normal > 1.4, $"{instant} {normal} {slow}");
        Assert.True(instant > 0 && slow == 1.0);
    }

    [Fact]
    public void Every_wait_shortens_with_the_speed()
    {
        var waits = new (string Name, Func<RevealSpeed, TimeSpan> Wait)[]
        {
            ("peek", s => s.Peek()),
            ("flight peek", s => s.FlightPeek()),
            ("notice peek", s => s.NoticePeek()),
            ("leave delay", s => s.LeaveDelay()),
            ("tuck hide delay", s => s.TuckHideDelay()),
        };
        foreach (var (name, wait) in waits)
        {
            var (instant, normal, slow) = (wait(RevealSpeed.Instant), wait(RevealSpeed.Normal), wait(RevealSpeed.Slow));
            Assert.True(instant <= normal && normal <= slow, $"{name}: {instant} {normal} {slow}");
            Assert.True(instant < slow, $"{name}: Instant must be quicker than Slow");
        }
        // Where no floor interferes they are strictly ordered.
        foreach (var wait in new Func<RevealSpeed, TimeSpan>[] { s => s.Peek(), s => s.NoticePeek(), s => s.TuckHideDelay() })
        {
            Assert.True(wait(RevealSpeed.Instant) < wait(RevealSpeed.Normal));
            Assert.True(wait(RevealSpeed.Normal) < wait(RevealSpeed.Slow));
        }
    }

    [Fact]
    public void A_peek_stays_long_enough_to_see()
    {
        foreach (var speed in All)
        {
            Assert.True(speed.Peek() >= Ms(1200));
            Assert.True(speed.FlightPeek() >= Ms(800));
            Assert.True(speed.LeaveDelay() >= Ms(120));
            // The capture's landing is seen before its board rests: the flight
            // (about 890 ms at Slow) plus a beat.
            Assert.True(speed.FlightPeek() > speed.Scaled(Ms(890)), speed.ToString());
        }
        Assert.Equal(Ms(1200), RevealSpeed.Instant.Peek());
        Assert.Equal(Ms(800), RevealSpeed.Instant.FlightPeek());
        Assert.Equal(Ms(120), RevealSpeed.Instant.LeaveDelay());
    }

    [Fact]
    public void A_notice_keeps_its_reading_time()
    {
        // Only what the quicker swing saves comes off.
        foreach (var speed in All)
        {
            Assert.Equal(Ms(600) - speed.Scaled(Ms(600)), Ms(1600) - speed.NoticePeek());
            Assert.True(speed.NoticePeek() > Ms(1000), speed.ToString());
        }
    }

    [Fact]
    public void The_window_hides_only_after_the_tuck_slide()
    {
        foreach (var speed in All)
        {
            var slide = speed.Scaled(Ms(RevealSpeedTimes.TuckMs));
            Assert.True(speed.TuckHideDelay() >= slide + RevealSpeedTimes.TuckMargin, speed.ToString());
            Assert.True(speed.TuckHideDelay() > slide);
        }
    }

    [Fact]
    public void Scaling_rounds_to_whole_milliseconds_and_never_reaches_zero()
    {
        foreach (var speed in All)
        {
            foreach (var d in new[] { Ms(1), Ms(2), Ms(3), Ms(190), Ms(3000) })
            {
                var scaled = speed.Scaled(d);
                Assert.True(scaled >= Ms(1), $"{speed} {d}");
                Assert.Equal(0, scaled.Ticks % TimeSpan.TicksPerMillisecond);
                Assert.True(scaled <= d);
            }
        }
        Assert.Equal(Ms(1), RevealSpeed.Instant.Scaled(Ms(1)));
        // A floor above the duration never lengthens it.
        Assert.Equal(Ms(100), RevealSpeed.Instant.ScaledMin(Ms(100), Ms(500)));
        Assert.Equal(RevealSpeed.Normal.Scaled(Ms(1000)), RevealSpeed.Normal.ScaledMin(Ms(1000), Ms(100)));
        Assert.Equal(Ms(600), RevealSpeed.Instant.ScaledMin(Ms(1000), Ms(600)));
    }

    [Fact]
    public void The_waits_are_the_milliseconds_tack_0_2_used()
    {
        // Pinned to Tack 0.2's arithmetic: halves round away from zero, and
        // 190 x 0.35 comes out at exactly 66.5 in doubles, so 67.
        int[] Row(RevealSpeed s) => [.. new[] { s.Peek(), s.FlightPeek(), s.NoticePeek(), s.LeaveDelay(), s.TuckHideDelay() }
            .Select(t => (int)t.TotalMilliseconds)];
        Assert.Equal([3000, 1750, 1600, 250, 300], Row(RevealSpeed.Slow));
        Assert.Equal([1950, 1138, 1390, 163, 234], Row(RevealSpeed.Normal));
        Assert.Equal([1200, 800, 1210, 120, 177], Row(RevealSpeed.Instant));
    }

    [Fact]
    public void It_is_saved_as_a_lowercase_word()
    {
        foreach (var (speed, text) in All.Zip(["instant", "normal", "slow"]))
        {
            Assert.Equal($"\"{text}\"", ConverterText.Write(new RevealSpeedJsonConverter(), speed));
            Assert.Equal(speed, ConverterText.Read(new RevealSpeedJsonConverter(), $"\"{text}\""));
            Assert.Equal(speed, RevealSpeedTimes.Parse(text));
        }
    }

    [Fact]
    public void Anything_else_is_not_a_speed()
    {
        foreach (var bad in new[] { "", "Normal", "NORMAL", " normal", "turbo", "fast", "0" })
        {
            Assert.Null(RevealSpeedTimes.Parse(bad));
            var json = JsonSerializer.Serialize(bad);
            Assert.Equal(RevealSpeed.Normal, ConverterText.Read(new RevealSpeedJsonConverter(), json));
        }
        foreach (var odd in new[] { "null", "7", "true", "[]", "{\"a\":1}" })
        {
            Assert.Equal(RevealSpeed.Normal, ConverterText.Read(new RevealSpeedJsonConverter(), odd));
        }
    }
}
