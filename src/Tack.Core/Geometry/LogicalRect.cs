namespace Tack.Core.Geometry;

/// <summary>A rectangle in logical (device-independent) pixels, as the board's window lays things out.</summary>
public readonly record struct LogicalRect(double X, double Y, double W, double H);
