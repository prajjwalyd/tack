namespace Tack.Core.Pinboard.Motion;

/// <summary>One print's share of a <see cref="Breeze"/>.</summary>
/// <param name="Index">The print's place among the prints in view, in row order.</param>
/// <param name="DelayMs">When it is pushed, ms after the breeze starts, at the current speed.</param>
/// <param name="Amplitude">The nudge, degrees, its sign the breeze's direction.</param>
public readonly record struct BreezePush(int Index, double DelayMs, double Amplitude);
