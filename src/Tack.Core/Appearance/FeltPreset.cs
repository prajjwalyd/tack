namespace Tack.Core.Appearance;

/// <summary>A board colour the user can pick from the list.</summary>
/// <param name="Id">What is saved: lowercase letters.</param>
/// <param name="Name">What the list shows.</param>
/// <param name="Hex">The felt in a light theme, "#rrggbb"; the board darkens it for a dark one.</param>
public sealed record FeltPreset(string Id, string Name, string Hex);
