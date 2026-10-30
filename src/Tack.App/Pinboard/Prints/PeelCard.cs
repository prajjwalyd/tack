using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Tack.Core.Pinboard;
using Windows.Foundation;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// Everything on a print's card, held so that a drag can peel it up from its
/// free edge: turned in depth about the pin's line, seen from in front of the
/// pin, with the curl's shading over the paper. Flat at rest, and with no
/// perspective then either: a card in perspective makes the compositor keep a
/// buffer to draw it into, even lying flat, and the board has a row of them.
/// </summary>
sealed class PeelCard
{
    /// <summary>
    /// The eye's distance: 520 px, less what the hinge's own perspective adds
    /// once the board is down (the card is not flattened before it).
    /// </summary>
    const double Perspective = 1 / (1 / 520.0 - 1 / BoardMetrics.Perspective);

    public PeelCard()
    {
        Visual = ElementCompositionPreview.GetElementVisual(Element);
        Visual.RotationAxis = Vector3.UnitX;
        Curl.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = BoardTheme.Rgba(0, 16, 8, 0), Offset = 0.35 },
                new GradientStop { Color = BoardTheme.Rgba(0, 16, 8, 0.10), Offset = 0.8 },
                new GradientStop { Color = BoardTheme.Rgba(0, 16, 8, 0.18), Offset = 1 },
            },
        };
        CurlVisual = ElementCompositionPreview.GetElementVisual(Curl);
    }

    /// <summary>Holds the card's pieces, the card's size.</summary>
    public Canvas Element { get; } = new();

    /// <summary>Turns about the X axis as the peel lifts.</summary>
    public Visual Visual { get; }

    /// <summary>The peeled paper's shading, darker toward the free edge; hidden at rest.</summary>
    public Border Curl { get; } = new() { IsHitTestVisible = false };

    public Visual CurlVisual { get; }

    /// <summary>Sizes it for a card <paramref name="w"/> by <paramref name="h"/>.</summary>
    public void Resize(double w, double h)
    {
        (Element.Width, Element.Height) = (w, h);
        (Curl.Width, Curl.Height) = (w, h);
        Visual.CenterPoint = new Vector3((float)(w / 2), (float)BoardMetrics.PinY, 0);
    }

    /// <summary>
    /// <paramref name="card"/> seen in depth from in front of the pin, while a
    /// peel lifts it (true), or flat again once it lies down (false).
    /// </summary>
    public void Depth(Visual card, bool on)
    {
        if (!on)
        {
            card.TransformMatrix = Matrix4x4.Identity;
            return;
        }
        var origin = Visual.CenterPoint;
        var depth = Matrix4x4.Identity;
        depth.M34 = (float)(-1 / Perspective);
        card.TransformMatrix = Matrix4x4.CreateTranslation(-origin) * depth * Matrix4x4.CreateTranslation(origin);
    }
}
