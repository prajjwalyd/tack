namespace Tack.Core.Updates;

/// <summary>The update row's one button.</summary>
/// <param name="Label">What it says, as "Check for updates".</param>
/// <param name="Act">What pressing it does.</param>
/// <param name="Accent">Drawn in the accent colour: the step the person is invited to take.</param>
/// <param name="Busy">Work is going on: it stays in place, keeps the focus and does nothing.</param>
public sealed record UpdateButton(string Label, UpdateAction Act, bool Accent = false, bool Busy = false);
