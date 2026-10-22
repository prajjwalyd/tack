using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Tack.Core.Pinboard;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// A print's pin: a glossy domed plastic head in its colour with a small
/// falling shadow, its contact shadow where it meets the paper, and over it a
/// polished brass head for a kept print, a little larger, across which a
/// glint runs. The heads and shadows are baked pictures (pins keep their
/// colours in high contrast); the glint is a band clipped to the brass.
/// </summary>
sealed class PinArt
{
    /// <summary>Each baked pin picture is this square, with the head's centre at (<see cref="CellX"/>, <see cref="CellY"/>).</summary>
    const double Cell = 28;
    const double CellX = 14;
    const double CellY = 12;
    const double Pin = BoardMetrics.PinSize;

    /// <summary>The glint's band, square around the brass head.</summary>
    public const double GlintSize = 1.8 * Pin;

    readonly ArtSprite _shadow = new(Cell, Cell);
    readonly ArtSprite _head = new(Cell, Cell);
    readonly ArtSprite _brass = new(Cell, Cell);

    public PinArt()
    {
        Root = new Canvas { Width = Pin, Height = Pin, IsHitTestVisible = false };
        var body = new Canvas { Width = Pin, Height = Pin };
        Body = ElementCompositionPreview.GetElementVisual(body);
        Body.CenterPoint = new Vector3((float)(Pin / 2), (float)(Pin / 2), 0);
        GlintBand = new Microsoft.UI.Xaml.Controls.Border
        {
            Width = GlintSize,
            Height = GlintSize,
            Background = AngledGradient.Make(115, GlintSize, GlintSize,
                (BoardTheme.Rgba(255, 252, 235, 0), 0.38), (BoardTheme.Rgba(255, 252, 235, 0.95), 0.5), (BoardTheme.Rgba(255, 252, 235, 0), 0.62)),
        };
        var glint = new Canvas { Width = Pin, Height = Pin, Children = { GlintBand } };
        Place(GlintBand, -(GlintSize - Pin) / 2, -(GlintSize - Pin) / 2);
        Place(glint, CellX - Pin / 2, CellY - Pin / 2);
        var clip = ElementCompositionPreview.GetElementVisual(glint);
        var circle = clip.Compositor.CreateEllipseGeometry();
        circle.Center = circle.Radius = new Vector2((float)(Pin / 2));
        clip.Clip = clip.Compositor.CreateGeometricClip(circle);
        Brass = new Canvas { Width = Cell, Height = Cell, Children = { _brass.Element, glint } };
        foreach (var piece in new[] { _shadow.Element, _head.Element, Brass })
        {
            Place(piece, Pin / 2 - CellX, Pin / 2 - CellY);
            body.Children.Add(piece);
        }
        Root.Children.Add(body);
    }

    /// <summary>The pin, the head's size, centred on its own middle.</summary>
    public Canvas Root { get; }

    /// <summary>Everything in the pin, scaled up about the head's middle while kept.</summary>
    public Visual Body { get; }

    /// <summary>The contact shadow's element, a baked cell.</summary>
    public Canvas Shadow => _shadow.Element;

    /// <summary>
    /// The contact shadow's own middle in its cell, for the dimple's scale: it
    /// reaches 2.5 pin px past the head on three sides and 3.5 below.
    /// </summary>
    public static Vector3 ShadowCentre { get; } = new((float)CellX, (float)(CellY + 0.5 * BoardMetrics.PinScale), 0);

    /// <summary>The brass head and its glint, a baked cell.</summary>
    public Canvas Brass { get; }

    /// <summary>The glint's band: it rests at <see cref="PrintView.GlintTravel"/> to the left, hidden.</summary>
    public Microsoft.UI.Xaml.Controls.Border GlintBand { get; }

    /// <summary>Draws the pin in <paramref name="color"/> for a theme, baked at <paramref name="scale"/>.</summary>
    public void Show(PinColor color, bool dark, double scale)
    {
        _head.Show(PrintArt.Themed($"pin-{color.ToString().ToLowerInvariant()}", dark), scale);
        _shadow.Show(PrintArt.Themed("pin-shadow", dark), scale);
        _brass.Show(PrintArt.Themed("brass", dark), scale);
    }

    static void Place(Microsoft.UI.Xaml.UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
    }
}
