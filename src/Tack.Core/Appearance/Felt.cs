namespace Tack.Core.Appearance;

/// <summary>
/// The board's colour, the user's "Board colour" setting: a preset, the
/// Windows accent colour, or a custom #rrggbb. Saved as one plain string: a
/// preset's id, "accent", or "#rrggbb" in lowercase.
/// </summary>
public abstract record Felt
{
    /// <summary>The saved text for the Windows accent colour.</summary>
    public const string AccentId = "accent";

    /// <summary>Every preset, in the order the list shows them. The first is the default.</summary>
    public static IReadOnlyList<FeltPreset> Presets { get; } =
    [
        new("green", "Green", "#24503f"),
        new("navy", "Navy", "#233a5e"),
        new("charcoal", "Charcoal", "#3b3a3e"),
        new("burgundy", "Burgundy", "#682b3b"),
        new("plum", "Plum", "#4a2f5c"),
        new("teal", "Teal", "#1f5560"),
        new("slate", "Slate", "#42586e"),
    ];

    public static Felt Default { get; } = new PresetFelt(Presets[0]);

    public static Felt Accent { get; } = new AccentFelt();

    Felt()
    {
    }

    /// <summary>A preset's id exactly, "accent", or "#rrggbb" in any case; anything else is null.</summary>
    public static Felt? TryParse(string text)
    {
        if (text == AccentId)
        {
            return Accent;
        }
        foreach (var preset in Presets)
        {
            if (preset.Id == text)
            {
                return new PresetFelt(preset);
            }
        }
        if (text.Length != 7 || text[0] != '#' || !text.Skip(1).All(char.IsAsciiHexDigit))
        {
            return null;
        }
        byte Channel(int at) => Convert.ToByte(text.Substring(at, 2), 16);
        return new CustomFelt(Channel(1), Channel(3), Channel(5));
    }

    /// <summary>As <see cref="TryParse"/>; anything unknown or malformed is the default.</summary>
    public static Felt Parse(string text) => TryParse(text) ?? Default;

    /// <summary>One of the colours on the list.</summary>
    public sealed record PresetFelt(FeltPreset Preset) : Felt
    {
        public override string ToString() => Preset.Id;
    }

    /// <summary>Whatever accent colour Windows has.</summary>
    public sealed record AccentFelt : Felt
    {
        public override string ToString() => AccentId;
    }

    /// <summary>The user's own colour.</summary>
    public sealed record CustomFelt(byte R, byte G, byte B) : Felt
    {
        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
    }
}
