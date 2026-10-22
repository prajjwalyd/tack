using Tack.Core.Shortcuts;

namespace Tack.Core.Tests.Shortcuts;

/// <summary>What a key press means to a shortcut field that is listening.</summary>
public class ChordCaptureTests
{
    const ushort Escape = 0x1B, Back = 0x08, Delete = 0x2E, Tab = 0x09, Enter = 0x0D, Control = 0x11, LeftWin = 0x5B;

    /// <summary>A key press: <paramref name="mods"/> is a string of the letters W, C, A, S.</summary>
    static KeyPress Press(ushort vk, string mods = "") =>
        new(vk, mods.Contains('W'), mods.Contains('C'), mods.Contains('A'), mods.Contains('S'));

    static CaptureStep Step(CaptureAct act, params string[] keys) => new(act, keys);

    static void Same(CaptureStep expected, CaptureStep actual)
    {
        Assert.Equal(expected.Act, actual.Act);
        Assert.Equal(expected.Keys, actual.Keys);
    }

    [Fact]
    public void Win_ctrl_or_alt_with_a_key_makes_a_chord_written_in_a_fixed_order()
    {
        var step = ChordCapture.Interpret(Press('S', "WA"));
        Same(Step(CaptureAct.Chord, "Win", "Alt", "S"), step);
        Assert.Equal("Win+Alt+S", step.Chord);
        Assert.Equal("Ctrl+Alt+Shift+K", ChordCapture.Interpret(Press('K', "SCA")).Chord);
        Same(Step(CaptureAct.Chord, "F9"), ChordCapture.Interpret(Press(0x78)));
        Same(Step(CaptureAct.Chord, "Ctrl", "7"), ChordCapture.Interpret(Press('7', "C")));
        Same(Step(CaptureAct.Chord, "Win", "Left"), ChordCapture.Interpret(Press(0x25, "W")));
    }

    [Fact]
    public void A_key_with_no_win_ctrl_or_alt_is_not_a_chord_but_gets_a_hint()
    {
        Same(Step(CaptureAct.Hint), ChordCapture.Interpret(Press('S')));
        Same(Step(CaptureAct.Hint), ChordCapture.Interpret(Press('S', "S")));
        Same(Step(CaptureAct.Hint), ChordCapture.Interpret(Press(Enter, "C")));
        // F25 does not exist on a keyboard but has a code: still no chord.
        Same(Step(CaptureAct.Hint), ChordCapture.Interpret(Press(0x88, "C")));
    }

    [Fact]
    public void Esc_cancels_backspace_and_delete_turn_it_off_tab_leaves_a_modifier_shows_what_is_held()
    {
        Same(Step(CaptureAct.Cancel), ChordCapture.Interpret(Press(Escape)));
        Same(Step(CaptureAct.Off), ChordCapture.Interpret(Press(Back)));
        Same(Step(CaptureAct.Off), ChordCapture.Interpret(Press(Delete)));
        Same(Step(CaptureAct.Leave), ChordCapture.Interpret(Press(Tab)));
        Same(Step(CaptureAct.Leave), ChordCapture.Interpret(Press(Tab, "S")));
        Same(Step(CaptureAct.Held, "Ctrl"), ChordCapture.Interpret(Press(Control, "C")));
        Same(Step(CaptureAct.Held, "Win", "Alt"), ChordCapture.Interpret(Press(LeftWin, "WA")));
        // With a modifier these are not the special keys.
        Same(Step(CaptureAct.Hint), ChordCapture.Interpret(Press(Escape, "C")));
        Same(Step(CaptureAct.Chord, "Alt", "Delete"), ChordCapture.Interpret(Press(Delete, "A")));
    }

    [Fact]
    public void Releasing_a_modifier_drops_it_from_what_is_held()
    {
        Assert.Equal(["Alt"], ChordCapture.HeldAfterRelease(Press(LeftWin, "WA")));
        Assert.Equal(["Win", "Alt"], ChordCapture.HeldAfterRelease(Press('A', "WA")));
        Assert.Equal(["Win"], ChordCapture.HeldAfterRelease(Press(0xA2, "WC")));
    }

    [Fact]
    public void A_chord_is_spoken_with_spaces_and_nothing_is_off()
    {
        Assert.Equal("Win + Alt + S", ChordCapture.Speak("Win+Alt+S"));
        Assert.Equal("off", ChordCapture.Speak(""));
    }

    [Fact]
    public void Every_chord_the_field_makes_parses_back_to_itself()
    {
        foreach (var vk in Enumerable.Range(0x08, 0x88 - 0x08))
        {
            var step = ChordCapture.Interpret(Press((ushort)vk, "WCAS"));
            if (step.Act == CaptureAct.Chord)
            {
                Assert.True(Chord.TryParse(step.Chord, out var chord, out var error), error);
                Assert.Equal(step.Chord, chord.ToString());
            }
        }
    }
}
