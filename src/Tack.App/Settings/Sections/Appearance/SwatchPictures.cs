using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tack.App.Pinboard.Chrome;
using Tack.Core.Appearance;
using Tack.Core.Pinboard.Look;
using Windows.Foundation;
using Windows.UI;

namespace Tack.App.Settings.Sections.Appearance;

/// <summary>
/// The 32 px pictures of the Appearance swatches: a disc of felt, the custom
/// colour wheel, a round piece of a frame's metal, and a little ruled note.
/// </summary>
static class SwatchPictures
{
    /// <summary>The size of every picture.</summary>
    public const double Size = 32;

    /// <summary>The note's rules, as on the board's notes: a red one, then two blue.</summary>
    static readonly (double Y, Rgba Colour)[] Rules =
    [
        (7, new Rgba(222, 72, 72, 0.62)),
        (15, new Rgba(84, 140, 214, 0.3)),
        (22, new Rgba(84, 140, 214, 0.3)),
    ];

    /// <summary>A disc of felt.</summary>
    public static UIElement Dot(Rgb felt) => new Ellipse { Width = Size, Height = Size, Fill = new SolidColorBrush(felt.ToColor()) };

    /// <summary>The custom swatch before a colour of the user's own is chosen.</summary>
    public static UIElement Wheel() => ConicFill.Disc(FeltChoices.Wheel, Size);

    /// <summary>
    /// A round piece of a frame's metal: its rail's tones from top to bottom,
    /// brushed bands turning round it, a soft highlight at the top left, a
    /// bright hairline along the top edge and a darker one along the bottom.
    /// </summary>
    public static UIElement Metal(FinishSwatch swatch, bool dark)
    {
        var metal = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        foreach (var stop in swatch.Metal)
        {
            metal.GradientStops.Add(new GradientStop { Offset = stop.At, Color = stop.Colour.ToColor() });
        }
        var shine = new RadialGradientBrush
        {
            Center = new Point(0.3, 0.18),
            GradientOrigin = new Point(0.3, 0.18),
            RadiusX = 0.56,
            RadiusY = 0.56,
        };
        shine.GradientStops.Add(new GradientStop { Offset = 0, Color = White(swatch.Shine) });
        shine.GradientStops.Add(new GradientStop { Offset = 1, Color = White(0) });
        var edges = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        edges.GradientStops.Add(new GradientStop { Offset = 0, Color = White(dark ? 0.4 : 0.7) });
        edges.GradientStops.Add(new GradientStop { Offset = 0.3, Color = White(0) });
        edges.GradientStops.Add(new GradientStop { Offset = 0.7, Color = new Rgba(0, 0, 0, 0).ToColor() });
        edges.GradientStops.Add(new GradientStop { Offset = 1, Color = new Rgba(0, 0, 0, 0.22).ToColor() });
        return new Grid
        {
            Width = Size,
            Height = Size,
            Children =
            {
                new Ellipse { Fill = metal },
                ConicFill.Disc(swatch.Grain, Size, FinishSwatch.GrainFrom),
                new Ellipse { Fill = shine },
                new Ellipse { Stroke = edges, StrokeThickness = 1 },
            },
        };
    }

    /// <summary>A little note of <paramref name="paper"/>, ruled like the board's notes.</summary>
    public static UIElement Note(Rgb paper)
    {
        var lines = new Canvas { Width = Size, Height = Size };
        foreach (var (y, colour) in Rules)
        {
            var rule = new Rectangle { Width = Size, Height = 1, Fill = new SolidColorBrush(colour.ToColor()) };
            Canvas.SetTop(rule, y);
            lines.Children.Add(rule);
        }
        return new Border
        {
            Width = Size,
            Height = Size,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(paper.ToColor()),
            Child = lines,
        };
    }

    static Color White(double alpha) => new Rgba(255, 255, 255, alpha).ToColor();
}
