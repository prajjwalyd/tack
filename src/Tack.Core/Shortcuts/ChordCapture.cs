namespace Tack.Core.Shortcuts;

/// <summary>
/// Chords as the Shortcuts section captures them: Win, Ctrl or Alt (with or
/// without Shift) and a key <see cref="ChordKeys"/> knows, or an F key alone,
/// written in <see cref="Chord"/>'s order. The rules are
/// <see cref="Chord.TryParse"/>'s, so whatever is captured parses.
/// </summary>
public static class ChordCapture
{
    /// <summary>Said under a field when a press is not a usable chord.</summary>
    public const string Hint = "Use Win, Ctrl or Alt with a letter, number or F-key.";

    const ushort Back = 0x08, Tab = 0x09, Escape = 0x1B, Delete = 0x2E;

    /// <summary>What a key press means to a field that is listening.</summary>
    public static CaptureStep Interpret(KeyPress press)
    {
        var held = Held(press);
        var plain = !press.Ctrl && !press.Alt && !press.Win;
        if (press.Vk == Tab && plain)
        {
            return new CaptureStep(CaptureAct.Leave, []);
        }
        if (ModifierOf(press.Vk) is not null)
        {
            return new CaptureStep(CaptureAct.Held, held);
        }
        if (held.Count == 0 && press.Vk == Escape)
        {
            return new CaptureStep(CaptureAct.Cancel, []);
        }
        if (held.Count == 0 && press.Vk is Back or Delete)
        {
            return new CaptureStep(CaptureAct.Off, []);
        }
        if (ChordKeys.Name(press.Vk) is not { } key || (plain && !ChordKeys.IsFunctionKey(press.Vk)))
        {
            return new CaptureStep(CaptureAct.Hint, []);
        }
        return new CaptureStep(CaptureAct.Chord, [.. held, key]);
    }

    /// <summary>The modifiers still held once <paramref name="release"/>'s key is up: the released one is not.</summary>
    public static IReadOnlyList<string> HeldAfterRelease(KeyPress release)
    {
        var released = ModifierOf(release.Vk);
        return [.. Held(release).Where(m => m != released)];
    }

    /// <summary>A chord for a screen reader: "Win + Alt + S", or "off".</summary>
    public static string Speak(string chord) => chord.Length > 0 ? string.Join(" + ", chord.Split('+')) : "off";

    /// <summary>The modifiers held, in a chord's order.</summary>
    static List<string> Held(KeyPress press)
    {
        var held = new List<string>(4);
        foreach (var (on, name) in new[] { (press.Win, "Win"), (press.Ctrl, "Ctrl"), (press.Alt, "Alt"), (press.Shift, "Shift") })
        {
            if (on)
            {
                held.Add(name);
            }
        }
        return held;
    }

    /// <summary>The modifier a key is, as a chord names it, or null for any other key.</summary>
    static string? ModifierOf(ushort vk) => vk switch
    {
        0x5B or 0x5C => "Win",
        0x11 or 0xA2 or 0xA3 => "Ctrl",
        0x12 or 0xA4 or 0xA5 => "Alt",
        0x10 or 0xA0 or 0xA1 => "Shift",
        _ => null,
    };
}
