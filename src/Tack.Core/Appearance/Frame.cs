namespace Tack.Core.Appearance;

/// <summary>
/// The board's frame, the user's "Frame" setting: a finish for the metal rails
/// around the felt. The finishes are tuned by hand, so there is no custom one.
/// Saved as the finish's id.
/// </summary>
public sealed record Frame(FramePreset Preset)
{
    /// <summary>
    /// Every finish, in the order the list shows them. The first is the default,
    /// the brushed aluminium the board has always had.
    /// </summary>
    public static IReadOnlyList<FramePreset> Presets { get; } =
    [
        new("aluminium", "Alloy"),
        new("graphite", "Graphite"),
        new("brass", "Brass"),
        new("silver", "Silver"),
    ];

    public static Frame Default { get; } = new(Presets[0]);

    public string Id => Preset.Id;

    /// <summary>A finish's id exactly; anything else is null.</summary>
    public static Frame? TryParse(string text)
    {
        foreach (var preset in Presets)
        {
            if (preset.Id == text)
            {
                return new Frame(preset);
            }
        }
        return null;
    }

    /// <summary>As <see cref="TryParse"/>; anything unknown is the default.</summary>
    public static Frame Parse(string text) => TryParse(text) ?? Default;

    public override string ToString() => Id;
}
