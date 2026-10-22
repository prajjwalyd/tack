namespace Tack.Core.Shortcuts;

/// <summary>
/// The keys a shortcut may end in: letters, digits, F1 to F24 and a few named
/// keys, with their Windows virtual-key codes.
/// </summary>
public static class ChordKeys
{
    const ushort F1 = 0x70;
    const ushort F24 = 0x87;

    static readonly (string Name, ushort Vk)[] Named =
    [
        ("Space", 0x20),
        ("PageUp", 0x21),
        ("PageDown", 0x22),
        ("End", 0x23),
        ("Home", 0x24),
        ("Left", 0x25),
        ("Up", 0x26),
        ("Right", 0x27),
        ("Down", 0x28),
        ("Insert", 0x2D),
        ("Delete", 0x2E),
    ];

    public static bool IsFunctionKey(ushort vk) => vk is >= F1 and <= F24;

    /// <summary>The key's name as written in a chord, or null if Tack does not accept it.</summary>
    public static string? Name(ushort vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= F1 and <= F24 => $"F{vk - 0x6F}",
        _ => Array.Find(Named, key => key.Vk == vk).Name,
    };

    /// <summary>The virtual-key code a name stands for, or null if Tack does not accept it.</summary>
    public static ushort? Code(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return upper[0];
        }
        if (upper.Length > 1 && upper[0] == 'F' && !upper.StartsWith("F0", StringComparison.Ordinal)
            && ushort.TryParse(upper.AsSpan(1), System.Globalization.NumberStyles.None, null, out var n) && n is >= 1 and <= 24)
        {
            return (ushort)(0x6F + n);
        }
        foreach (var (named, vk) in Named)
        {
            if (string.Equals(named, name, StringComparison.OrdinalIgnoreCase))
            {
                return vk;
            }
        }
        return null;
    }
}
