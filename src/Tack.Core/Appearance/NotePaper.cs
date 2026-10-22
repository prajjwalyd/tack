namespace Tack.Core.Appearance;

/// <summary>
/// The colour of the paper notes, the user's "Note paper" setting: one from a
/// short list, every one pale enough that the dark ink stays easy to read.
/// Saved as the colour's id.
/// </summary>
public sealed record NotePaper(PaperPreset Preset)
{
    /// <summary>
    /// Every colour, in the order the list shows them. The first is the default,
    /// the white index card the notes have always been.
    /// </summary>
    public static IReadOnlyList<PaperPreset> Presets { get; } =
    [
        new("white", "White", "#fffefa"),
        new("cream", "Cream", "#faf0d7"),
        new("butter", "Butter", "#fff0a8"),
        new("blush", "Blush", "#fadadd"),
        new("sky", "Sky", "#d6e8fa"),
        new("mint", "Mint", "#d5f0de"),
    ];

    public static NotePaper Default { get; } = new(Presets[0]);

    public string Id => Preset.Id;

    /// <summary>A colour's id exactly; anything else is null.</summary>
    public static NotePaper? TryParse(string text)
    {
        foreach (var preset in Presets)
        {
            if (preset.Id == text)
            {
                return new NotePaper(preset);
            }
        }
        return null;
    }

    /// <summary>As <see cref="TryParse"/>; anything unknown is the default.</summary>
    public static NotePaper Parse(string text) => TryParse(text) ?? Default;

    public override string ToString() => Id;
}
