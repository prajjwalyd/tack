namespace Tack.Core.Appearance;

/// <summary>A frame finish the user can pick from the list.</summary>
/// <param name="Id">What is saved: lowercase letters.</param>
/// <param name="Name">What the list shows.</param>
public sealed record FramePreset(string Id, string Name);
