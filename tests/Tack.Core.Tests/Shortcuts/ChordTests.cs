using Tack.Core.Shortcuts;

namespace Tack.Core.Tests.Shortcuts;

public class ChordTests
{
    static Chord Parse(string text)
    {
        Assert.True(Chord.TryParse(text, out var chord, out var error), error);
        return Assert.NotNull(chord);
    }

    [Fact]
    public void The_defaults_parse()
    {
        Assert.Equal(new Chord(Win: true, Ctrl: false, Alt: true, Shift: false, Vk: 0x53), Parse(Chord.DefaultToggle));
        Assert.Equal(0x43, Parse(Chord.DefaultPin).Vk);
        Assert.Equal(Chord.DefaultPin, Parse(Chord.DefaultPin).ToString());
    }

    [Fact]
    public void Chords_print_in_one_order_whatever_the_input()
    {
        Assert.Equal("Win+Ctrl+Alt+Shift+K", Parse("shift + alt+ctrl+WINDOWS+k").ToString());
        Assert.Equal("Ctrl+Alt+T", Parse("control+alt+t").ToString());
        Assert.Equal("Win+Alt+C", Chord.Tidy(" alt+win+c "));
        Assert.Equal("nonsense", Chord.Tidy("nonsense"));
        Assert.Equal("", Chord.Tidy("  "));
    }

    [Fact]
    public void Letters_digits_f_keys_and_a_few_named_keys_are_accepted()
    {
        Assert.Equal(0x37, Parse("Ctrl+7").Vk);
        Assert.Equal(0x78, Parse("F9").Vk);
        Assert.Equal(0x87, Parse("Shift+F24").Vk);
        Assert.Equal("Win+Alt+PageUp", Parse("Win+Alt+pageup").ToString());
        Assert.Equal("Ctrl+Alt+Space", Parse("Ctrl+Alt+Space").ToString());
        var keys = Enumerable.Range(0x30, 10).Concat(Enumerable.Range(0x41, 26)).Concat(Enumerable.Range(0x70, 24));
        foreach (var vk in keys)
        {
            var name = ChordKeys.Name((ushort)vk)!;
            Assert.Equal(vk, Parse($"Alt+{name}").Vk);
        }
    }

    [Fact]
    public void Off_and_bad_chords()
    {
        Assert.True(Chord.TryParse("", out var off, out _));
        Assert.Null(off);
        foreach (var bad in new[] { "T", "Shift+T", "Win+Alt", "Win+Alt+S+C", "Win++T", "Ctrl+F0", "Ctrl+F25", "Ctrl+Tab", "Alt+é" })
        {
            Assert.False(Chord.TryParse(bad, out _, out _), bad);
        }
    }
}
