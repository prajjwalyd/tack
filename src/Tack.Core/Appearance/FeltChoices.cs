using Tack.Core.Pinboard.Look;

namespace Tack.Core.Appearance;

/// <summary>
/// The board colour card's swatches: one per preset, one that follows the
/// Windows accent colour, and a custom one that opens a colour picker. Says
/// which swatch a felt is, what the card's head names it, and where the
/// picker starts.
/// </summary>
public static class FeltChoices
{
    /// <summary>The value of the custom swatch, which stands for any #rrggbb.</summary>
    public const string CustomValue = "custom";

    /// <summary>How many columns every Appearance card's swatches are laid out in: the board colours need the most.</summary>
    public const int Columns = 9;

    /// <summary>Every swatch, in order: the presets, Accent, then Custom.</summary>
    public static IReadOnlyList<SwatchChoice> All { get; } =
    [
        .. Felt.Presets.Select(p => new SwatchChoice(p.Id, p.Name, p.Name)),
        new(Felt.AccentId, "Accent", "Windows accent colour"),
        new(CustomValue, "Custom", "Custom colour", Manual: true),
    ];

    /// <summary>The custom swatch until a custom colour is chosen: a colour wheel, going round once.</summary>
    public static IReadOnlyList<SwatchStop> Wheel { get; } = Spread("#f2493a", "#f7b731", "#7ed321", "#2bc4d8", "#4a6cf7", "#b34ae0", "#f2493a");

    /// <summary>Which swatch <paramref name="felt"/> is: a preset's id, "accent", or "custom" for any colour of the user's own.</summary>
    public static string ValueOf(Felt felt) => felt is Felt.CustomFelt ? CustomValue : felt.ToString();

    /// <summary>What the card's head says for <paramref name="felt"/>: the swatch's name, or "Custom #rrggbb".</summary>
    public static string NameOf(Felt felt) => felt switch
    {
        Felt.CustomFelt custom => $"Custom {custom}",
        _ => All.FirstOrDefault(c => c.Value == felt.ToString())?.Name ?? "",
    };

    /// <summary>The felt a swatch picks; null for the custom one, which opens the picker instead.</summary>
    public static Felt? FeltOf(string value) => value.StartsWith('#') ? null : Felt.TryParse(value);

    /// <summary>
    /// Where the picker starts: on the custom colour last chosen, else on the
    /// colour the board has (a preset's own, or a custom one), else on the first preset.
    /// </summary>
    public static Felt.CustomFelt PickerStart(Felt current, Felt.CustomFelt? lastCustom)
    {
        if (lastCustom is not null)
        {
            return lastCustom;
        }
        var hex = current switch
        {
            Felt.CustomFelt custom => custom.ToString(),
            Felt.PresetFelt preset => preset.Preset.Hex,
            _ => Felt.Presets[0].Hex,
        };
        return (Felt.CustomFelt)Felt.Parse(hex);
    }

    static SwatchStop[] Spread(params string[] hexes) =>
        [.. hexes.Select((hex, n) => new SwatchStop(n / (double)(hexes.Length - 1), Rgb.Parse(hex).At(1)))];
}
