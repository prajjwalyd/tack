namespace Tack.Core.Shortcuts;

/// <summary>A key going down or up in a shortcut field, with the modifiers held at the time.</summary>
/// <param name="Vk">The key's Windows virtual-key code.</param>
public readonly record struct KeyPress(ushort Vk, bool Win, bool Ctrl, bool Alt, bool Shift);
