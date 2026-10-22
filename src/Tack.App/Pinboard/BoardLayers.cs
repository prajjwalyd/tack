using Microsoft.UI.Xaml.Controls;

namespace Tack.App.Pinboard;

/// <summary>
/// The board window's fixed elements (BoardWindow.xaml), outermost first.
/// The window is the monitor's work area; y 0 is the screen edge.
/// </summary>
/// <param name="Root">The whole window, see-through.</param>
/// <param name="Stage">Centred at the top, as wide as the board; carries the hinge's perspective.</param>
/// <param name="Board">The hinged board, 174 tall at y -24: turns about (50%, 24) to fold up under the edge.</param>
/// <param name="Cork">The felt and frame, the board's whole size.</param>
/// <param name="Rail">The row's viewport, from the screen edge 360 down into the overhang.</param>
/// <param name="Track">The row itself, at the board's centre: prints sit at x relative to it; scrolling moves it.</param>
/// <param name="Veil">The unlit veil over everything on the board, shown while folded.</param>
/// <param name="Overlay">Over the window, not hinged: flights, the notice, the tip.</param>
sealed record BoardLayers(
    Grid Root, Canvas Stage, Canvas Board, Border Cork, Canvas Rail, Canvas Track, Border Veil, Canvas Overlay);
