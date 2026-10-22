using Tack.Core.Appearance;

namespace Tack.Core.Tests.Appearance;

/// <summary>A colour dragged in the picker: sent at most once a beat, and the last one when the picker closes.</summary>
public class ColourDragTests
{
    static readonly Felt.CustomFelt Start = new(10, 20, 30);
    static readonly Felt.CustomFelt A = new(1, 1, 1);
    static readonly Felt.CustomFelt B = new(2, 2, 2);
    static readonly Felt.CustomFelt C = new(3, 3, 3);

    static ColourDrag Opened()
    {
        var drag = new ColourDrag();
        drag.Open(Start);
        return drag;
    }

    [Fact]
    public void It_waits_the_old_beat() => Assert.Equal(TimeSpan.FromMilliseconds(120), ColourDrag.Beat);

    [Fact]
    public void The_first_move_asks_for_a_beat_and_the_rest_wait_for_it()
    {
        var drag = Opened();
        Assert.True(drag.Move(A));
        Assert.False(drag.Move(B));
        Assert.False(drag.Move(C));
        Assert.Equal(C, drag.Due());
        Assert.Null(drag.Due());
    }

    [Fact]
    public void After_a_beat_the_next_move_asks_for_another()
    {
        var drag = Opened();
        drag.Move(A);
        drag.Due();
        Assert.True(drag.Move(B));
        Assert.Equal(B, drag.Due());
    }

    [Fact]
    public void The_colour_it_opened_on_or_last_sent_is_no_move()
    {
        var drag = Opened();
        Assert.False(drag.Move(Start));
        Assert.Null(drag.Due());
        drag.Move(A);
        drag.Due();
        Assert.False(drag.Move(A));
        Assert.Null(drag.Close());
    }

    [Fact]
    public void Closing_sends_a_colour_still_waiting_and_nothing_more()
    {
        var drag = Opened();
        drag.Move(A);
        drag.Move(B);
        Assert.Equal(B, drag.Close());
        Assert.Null(drag.Due());
        Assert.Null(drag.Close());
    }

    [Fact]
    public void Closing_untouched_sends_nothing() => Assert.Null(Opened().Close());

    [Fact]
    public void Opening_again_forgets_what_was_waiting()
    {
        var drag = Opened();
        drag.Move(A);
        drag.Open(B);
        Assert.Null(drag.Due());
        Assert.False(drag.Move(B));
    }
}
