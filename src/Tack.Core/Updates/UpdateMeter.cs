namespace Tack.Core.Updates;

/// <summary>The bar under the update row while something downloads or installs.</summary>
/// <param name="Fraction">How much is done, 0 to 1; null when that is not known, and the bar slides along instead.</param>
public sealed record UpdateMeter(double? Fraction);
