using Tack.Core.Shortcuts;

namespace Tack.Core.Tests.Shortcuts;

/// <summary>Checking a pair of shortcuts before they are registered, and what the Shortcuts section says about one.</summary>
public class ShortcutPlanTests
{
    [Fact]
    public void Two_good_chords_are_tidied_and_may_be_tried()
    {
        var plan = ShortcutPlan.For(" alt+win+s ", "ctrl+alt+p");
        Assert.True(plan.CanTry);
        Assert.Equal(new ShortcutState("Win+Alt+S", ShortcutStatus.Ok), plan.Answer.Toggle);
        Assert.Equal(new ShortcutState("Ctrl+Alt+P", ShortcutStatus.Ok), plan.Answer.Pin);
        Assert.Equal("Win+Alt+S", plan.Toggle.ToString());
    }

    [Fact]
    public void Empty_turns_one_off()
    {
        var plan = ShortcutPlan.For("", "Win+Alt+C");
        Assert.True(plan.CanTry);
        Assert.Null(plan.Toggle);
        Assert.Equal(new ShortcutState("", ShortcutStatus.Off), plan.Answer.Toggle);
    }

    [Fact]
    public void A_bad_chord_is_invalid_and_nothing_is_tried()
    {
        var plan = ShortcutPlan.For("Shift+T", "Win+Alt+C");
        Assert.False(plan.CanTry);
        Assert.Equal(new ShortcutState("Shift+T", ShortcutStatus.Invalid), plan.Answer.Toggle);
        Assert.Equal(ShortcutStatus.Ok, plan.Answer.Pin.Status);
    }

    [Fact]
    public void Both_on_one_chord_makes_the_second_taken_by_tack_itself()
    {
        var plan = ShortcutPlan.For("Win+Alt+S", "alt+win+s");
        Assert.False(plan.CanTry);
        Assert.Equal(ShortcutStatus.Ok, plan.Answer.Toggle.Status);
        Assert.Equal(new ShortcutState("Win+Alt+S", ShortcutStatus.InUse), plan.Answer.Pin);
        // Both off is fine.
        Assert.True(ShortcutPlan.For("", "").CanTry);
    }

    [Fact]
    public void The_status_line_says_what_is_wrong()
    {
        Assert.Equal("Win+Alt+S is in use by another app", ShortcutState.Describe(new("Win+Alt+S", ShortcutStatus.InUse)));
        Assert.Equal("In use by another app", ShortcutState.Describe(new("", ShortcutStatus.InUse)));
        Assert.Equal(ChordCapture.Hint, ShortcutState.Describe(new("Shift+T", ShortcutStatus.Invalid)));
        Assert.Equal("", ShortcutState.Describe(new("Win+Alt+S", ShortcutStatus.Ok)));
        Assert.Equal("", ShortcutState.Describe(new("", ShortcutStatus.Off)));
    }

    [Fact]
    public void A_pair_works_when_neither_is_taken_or_invalid()
    {
        Assert.True(new ShortcutPair(new("Win+Alt+S", ShortcutStatus.Ok), new("", ShortcutStatus.Off)).AllWork);
        Assert.False(new ShortcutPair(new("Win+Alt+S", ShortcutStatus.Ok), new("Win+Alt+C", ShortcutStatus.InUse)).AllWork);
    }
}
