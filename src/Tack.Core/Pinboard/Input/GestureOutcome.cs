namespace Tack.Core.Pinboard.Input;

/// <summary>A gesture finished: what it asks for, and of which print.</summary>
public readonly record struct GestureOutcome(GestureAction Action, string Id);
