namespace Tack.Core.Prints;

/// <summary>What keeping, or no longer keeping, a print did.</summary>
/// <param name="Print">The print as it is now.</param>
/// <param name="Moved">The row's order changed.</param>
/// <param name="Aged">
/// Prints that aged out because of it: a print that is no longer kept rejoins
/// the history, and may be past its limits.
/// </param>
public sealed record KeepChange(Print Print, bool Moved, IReadOnlyList<Print> Aged);
