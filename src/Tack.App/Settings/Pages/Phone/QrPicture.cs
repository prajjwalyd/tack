using Microsoft.UI.Xaml.Media;
using Tack.Core.Phone;
using Windows.Foundation;

namespace Tack.App.Settings.Pages.Phone;

/// <summary>A QR code as one shape to fill: a rectangle per run of dark modules along each row, one unit per module.</summary>
static class QrPicture
{
    /// <summary>
    /// Each row's runs are drawn a hair taller than a module, overlapping the
    /// row below, so no seam of the light background shows between rows when
    /// the code is scaled.
    /// </summary>
    const double Overlap = 0.05;

    public static Geometry Of(QrMatrix qr)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var y = 0; y < qr.Size; y++)
        {
            var height = y < qr.Size - 1 ? 1 + Overlap : 1;
            for (var x = 0; x < qr.Size; x++)
            {
                if (!qr.IsDark(x, y))
                {
                    continue;
                }
                var start = x;
                while (x + 1 < qr.Size && qr.IsDark(x + 1, y))
                {
                    x++;
                }
                group.Children.Add(new RectangleGeometry { Rect = new Rect(start, y, x + 1 - start, height) });
            }
        }
        // The finder squares fill three corners, so the shape spans the whole code and stretches to the tile as a square.
        return group;
    }
}
