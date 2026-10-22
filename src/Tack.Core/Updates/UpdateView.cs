namespace Tack.Core.Updates;

/// <summary>What the update row shows for a status.</summary>
/// <param name="Title">The headline, as "Tack is up to date".</param>
/// <param name="Sub">The line under it; empty for none.</param>
/// <param name="Button">The one button that fits.</param>
/// <param name="Meter">The progress bar; null for none.</param>
public sealed record UpdateView(string Title, string Sub, UpdateButton Button, UpdateMeter? Meter = null);
