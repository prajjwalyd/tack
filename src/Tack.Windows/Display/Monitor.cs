using Tack.Core.Geometry;

namespace Tack.Windows.Display;

/// <summary>A monitor, in physical pixels.</summary>
/// <param name="Work">The monitor without the taskbar.</param>
/// <param name="Bounds">The whole monitor.</param>
/// <param name="Scale">Its DPI over 96.</param>
public readonly record struct Monitor(Rect Work, Rect Bounds, double Scale);
