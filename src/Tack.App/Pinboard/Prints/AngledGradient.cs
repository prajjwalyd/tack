using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// A straight gradient at an angle across a box: 0 degrees runs up, 90 to the
/// right, and the line is just long enough for the box's corners to meet the
/// first and last stops, so the bands keep their angle on any box.
/// </summary>
static class AngledGradient
{
    /// <summary>A gradient at <paramref name="degrees"/> across a <paramref name="w"/> by <paramref name="h"/> box, its stops as (colour, 0 to 1).</summary>
    public static LinearGradientBrush Make(double degrees, double w, double h, params (Color Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute };
        foreach (var (color, at) in stops)
        {
            brush.GradientStops.Add(new GradientStop { Color = color, Offset = at });
        }
        Aim(brush, degrees, w, h);
        return brush;
    }

    /// <summary>Lays <paramref name="brush"/> across a box of a new size.</summary>
    public static void Aim(LinearGradientBrush brush, double degrees, double w, double h)
    {
        var angle = degrees * Math.PI / 180;
        var (dx, dy) = (Math.Sin(angle), -Math.Cos(angle));
        var half = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2;
        brush.StartPoint = new Point(w / 2 - dx * half, h / 2 - dy * half);
        brush.EndPoint = new Point(w / 2 + dx * half, h / 2 + dy * half);
    }
}
