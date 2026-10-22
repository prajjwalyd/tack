namespace Tack.Core.Appearance;

/// <summary>A note paper colour the user can pick from the list.</summary>
/// <param name="Id">What is saved: lowercase letters.</param>
/// <param name="Name">What the list shows.</param>
/// <param name="Hex">The paper in a light theme, "#rrggbb"; the board dims it for a dark one.</param>
public sealed record PaperPreset(string Id, string Name, string Hex);
