namespace Tack.Core.Appearance;

/// <summary>One swatch of an Appearance card.</summary>
/// <param name="Value">What choosing it picks: a preset's id, "accent" or "custom".</param>
/// <param name="Name">The word under it.</param>
/// <param name="Label">What a screen reader calls it.</param>
/// <param name="Manual">It opens a picker rather than choosing: the arrow keys move onto it without choosing it.</param>
public sealed record SwatchChoice(string Value, string Name, string Label, bool Manual = false);
