using Tack.Core.Pinboard.Input;

namespace Tack.Core.Tests.Pinboard.Input;

public class GestureMachineTests
{
    const PointerButton Left = PointerButton.Left;

    static GestureOutcome Copy(string id) => new(GestureAction.Copy, id);

    static GestureOutcome Open(string id) => new(GestureAction.Open, id);

    static GestureOutcome Edit(string id) => new(GestureAction.Edit, id);

    static GestureOutcome Drag(string id) => new(GestureAction.StartDrag, id);

    static GestureOutcome Menu(string id) => new(GestureAction.Menu, id);

    /// <summary>A press and release of the left button on <paramref name="id"/>, at <paramref name="at"/>, held for <paramref name="held"/> ms.</summary>
    static List<GestureOutcome> Click(GestureMachine m, string id, long at, long held = 80)
    {
        var outcomes = m.Press(id, Left, 10, 10, at).ToList();
        outcomes.AddRange(m.Release(Left, at + held));
        return outcomes;
    }

    [Fact]
    public void The_gesture_times_are_the_old_board_s()
    {
        Assert.Equal(260, GestureMachine.DoubleClickMs);
        Assert.Equal(520, GestureMachine.HoldMs);
        Assert.Equal(4, GestureMachine.DragPx);
        Assert.Equal(600, GestureMachine.KeyboardMenuMs);
    }

    [Fact]
    public void A_click_copies_only_once_the_double_click_window_has_passed()
    {
        var m = new GestureMachine();
        Assert.Empty(Click(m, "a", 0));
        Assert.Equal("a", m.PendingCopy);
        Assert.Equal(80 + 260, m.NextDue);
        Assert.Empty(m.Due(339));
        Assert.Equal([Copy("a")], m.Due(340));
        Assert.Null(m.PendingCopy);
        Assert.Null(m.NextDue);
        Assert.Empty(m.Due(1000));
    }

    [Fact]
    public void A_second_press_within_the_window_opens_instead_of_copying()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        Assert.Equal([Open("a")], m.Press("a", Left, 10, 10, 80 + 259));
        // The second press is spent on opening: its release is no click, and nothing copies.
        Assert.False(m.Sinking);
        Assert.Empty(m.Release(Left, 400));
        Assert.Null(m.PendingCopy);
        Assert.Empty(m.Due(5000));
    }

    [Fact]
    public void A_second_press_just_after_the_window_copies_and_starts_over()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        // The timer has not been heard yet: the copy that was due is made first.
        Assert.Equal([Copy("a")], m.Press("a", Left, 10, 10, 80 + 260));
        Assert.True(m.Sinking);
    }

    [Fact]
    public void A_press_on_another_print_copies_the_waiting_click_at_once()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        Assert.Equal([Copy("a")], m.Press("b", Left, 10, 10, 100));
        Assert.Null(m.PendingCopy);
        Assert.Empty(m.Release(Left, 150));
        Assert.Equal("b", m.PendingCopy);
        Assert.Equal([Copy("b")], m.Due(150 + 260));
    }

    [Fact]
    public void Holding_520_ms_edits_and_is_no_click()
    {
        var m = new GestureMachine();
        Assert.Empty(m.Press("a", Left, 10, 10, 1000));
        Assert.True(m.Sinking);
        Assert.Equal(1520, m.NextDue);
        Assert.Empty(m.Due(1519));
        Assert.Equal([Edit("a")], m.Due(1520));
        // The hold is over: the print stops sinking, though the button is still down.
        Assert.Equal("a", m.PressId);
        Assert.False(m.Sinking);
        Assert.Null(m.NextDue);
        Assert.Empty(m.Release(Left, 1700));
        Assert.Null(m.PressId);
        Assert.Null(m.PendingCopy);
    }

    [Fact]
    public void A_release_heard_after_the_hold_was_due_still_edits()
    {
        var m = new GestureMachine();
        m.Press("a", Left, 10, 10, 0);
        Assert.Equal([Edit("a")], m.Release(Left, 600));
        Assert.Null(m.PendingCopy);
    }

    [Fact]
    public void Moving_more_than_4_px_starts_a_drag_and_never_copies_or_edits()
    {
        var m = new GestureMachine();
        m.Press("a", Left, 100, 100, 0);
        Assert.Empty(m.Move(104, 100, 50));
        Assert.Empty(m.Move(102.8, 102.8, 60));
        Assert.Equal([Drag("a")], m.Move(103, 103, 70));
        Assert.True(m.Dragged);
        Assert.False(m.Sinking);
        Assert.Null(m.NextDue);
        Assert.Empty(m.Move(200, 200, 80));
        Assert.Empty(m.Due(2000));
        Assert.Empty(m.Release(Left, 2100));
        Assert.Null(m.PendingCopy);
    }

    [Fact]
    public void A_drag_s_press_outlives_a_lost_capture_and_ends_with_the_drag()
    {
        var m = new GestureMachine();
        m.Press("a", Left, 0, 0, 0);
        m.Move(10, 0, 20);
        m.Cancel();
        Assert.Equal("a", m.PressId);
        m.EndPress();
        Assert.Null(m.PressId);
    }

    [Fact]
    public void A_cancelled_press_is_no_click()
    {
        var m = new GestureMachine();
        m.Press("a", Left, 0, 0, 0);
        m.Cancel();
        Assert.Null(m.PressId);
        Assert.Empty(m.Release(Left, 50));
        Assert.Empty(m.Due(1000));
    }

    [Fact]
    public void A_move_after_a_hold_does_not_drag()
    {
        var m = new GestureMachine();
        m.Press("a", Left, 0, 0, 0);
        Assert.Equal([Edit("a")], m.Move(50, 0, 530));
        Assert.Empty(m.Move(60, 0, 540));
    }

    [Fact]
    public void Right_middle_and_other_buttons_never_press()
    {
        var m = new GestureMachine();
        foreach (var button in new[] { PointerButton.Right, PointerButton.Middle, PointerButton.Other })
        {
            Assert.Empty(m.Press("a", button, 0, 0, 0));
            Assert.Null(m.PressId);
        }
        Assert.Empty(m.Due(5000));
    }

    [Fact]
    public void Letting_go_of_another_button_is_no_click()
    {
        var m = new GestureMachine();
        m.Press("a", Left, 0, 0, 0);
        Assert.Empty(m.Release(PointerButton.Right, 50));
        Assert.Null(m.PressId);
        Assert.Null(m.PendingCopy);
    }

    [Fact]
    public void A_right_click_opens_the_menu_and_drops_that_print_s_waiting_click()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        Assert.Equal([Menu("a")], m.Menu("a", 100));
        Assert.Null(m.PendingCopy);
        Assert.Empty(m.Due(1000));
    }

    [Fact]
    public void A_right_click_on_another_print_leaves_the_waiting_click_to_copy()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        Assert.Equal([Menu("b")], m.Menu("b", 100));
        Assert.Equal([Copy("a")], m.Due(340));
    }

    [Fact]
    public void A_right_click_ends_a_press_on_the_print()
    {
        var m = new GestureMachine();
        m.Press("a", Left, 0, 0, 0);
        Assert.Equal([Menu("a")], m.Menu("a", 100));
        Assert.Null(m.PressId);
        Assert.Empty(m.Due(1000));
    }

    [Fact]
    public void No_pointer_menu_for_600_ms_after_the_keyboard_asked_for_one()
    {
        var m = new GestureMachine();
        m.KeyboardMenuAsked(1000);
        Assert.Empty(m.Menu("a", 1000));
        Assert.Empty(m.Menu("a", 1599));
        Assert.Equal([Menu("a")], m.Menu("a", 1600));
    }

    [Fact]
    public void Flushing_copies_the_waiting_click_now_and_ends_the_press()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        m.Press("b", Left, 0, 0, 90);
        Assert.Equal("b", m.PressId);
        var m2 = new GestureMachine();
        Click(m2, "c", 0);
        Assert.Equal([Copy("c")], m2.Flush(100));
        Assert.Null(m2.NextDue);
        Assert.Empty(m.Flush(100));
        Assert.Null(m.PressId);
        Assert.Null(m.NextDue);
    }

    [Fact]
    public void A_print_forgotten_loses_its_press_and_its_waiting_click()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        m.Forget("a");
        Assert.Null(m.PendingCopy);
        m.Press("b", Left, 0, 0, 500);
        m.Forget("a");
        Assert.Equal("b", m.PressId);
        m.Forget("b");
        Assert.Null(m.PressId);
        Assert.Empty(m.Due(5000));
    }

    [Fact]
    public void The_next_due_time_is_the_sooner_of_the_copy_and_the_hold()
    {
        var m = new GestureMachine();
        Click(m, "a", 0);
        m.Press("a", Left, 0, 0, 400);
        Assert.Equal(920, m.NextDue);
        m.Release(Left, 450);
        Assert.Equal(710, m.NextDue);
    }
}
