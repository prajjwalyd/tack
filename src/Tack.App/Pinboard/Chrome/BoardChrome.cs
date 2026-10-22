using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Tack.App.Pinboard.Contract;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;

namespace Tack.App.Pinboard.Chrome;

/// <summary>
/// The board's material: the felt (the user's colour, its wash, vignette and
/// wool), the metal frame in the chosen finish with its corner caps, the
/// shade where it tucks under the screen edge and its drop shadow; plain
/// colours in high contrast. All compositor visuals hung on the cork, bottom
/// up: the shadow and ring, the rails, the felt, the caps, the inner shade,
/// the tuck shade. Follows the look, the theme and the board's width live,
/// and hands the theme the user's felt and paper so the prints follow them too.
/// </summary>
sealed class BoardChrome : BoardPart
{
    readonly ContainerVisual _root;
    readonly BoardShadow _shadow;
    readonly FrameRails _rails;
    readonly FeltLayer _felt;
    readonly CornerCap _leftCap;
    readonly CornerCap _rightCap;
    readonly InnerShade _innerShade;
    readonly TuckShade _tuckShade;

    public BoardChrome(BoardContext board) : base(board)
    {
        var cork = board.Layers.Cork;
        var compositor = ElementCompositionPreview.GetElementVisual(cork).Compositor;
        _root = compositor.CreateContainerVisual();
        _shadow = new BoardShadow(compositor);
        _rails = new FrameRails(compositor);
        _felt = new FeltLayer(compositor);
        _leftCap = new CornerCap(compositor, mirrored: false);
        _rightCap = new CornerCap(compositor, mirrored: true);
        _innerShade = new InnerShade(compositor);
        _tuckShade = new TuckShade(compositor);
        Visual[] layers = [_shadow.Visual, _rails.Visual, _felt.Visual, _leftCap.Visual, _rightCap.Visual, _innerShade.Visual, _tuckShade.Visual];
        foreach (var layer in layers)
        {
            _root.Children.InsertAtTop(layer);
        }
        ElementCompositionPreview.SetElementChildVisual(cork, _root);
        cork.CornerRadius = new CornerRadius(0, 0, BoardMetrics.BoardRadius, BoardMetrics.BoardRadius);
        // Set by the board's geometry as the row grows or shrinks; heard at once, not after layout.
        cork.RegisterPropertyChangedCallback(FrameworkElement.WidthProperty, (_, _) => Resize());
        Resize();
        Paint();    }

    public override void ThemeChanged() => Paint();

    public override void LookChanged(BoardLook look)
    {
        // A new felt or paper changes the theme, which repaints everything; a new frame alone is the chrome's.
        if (!Board.Theme.Follow(look.Felt, look.Paper))
        {
            Paint();
        }
    }

    void Resize()
    {
        var width = Board.Layers.Cork.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }
        _shadow.Resize(width);
        _rails.Resize(width);
        _felt.Resize(width);
        var capTop = (float)(BoardMetrics.ElementHeight - BoardMetrics.CapSize);
        _leftCap.Visual.Offset = new Vector3(0, capTop, 0);
        _rightCap.Visual.Offset = new Vector3((float)(width - BoardMetrics.CapSize), capTop, 0);
        _innerShade.Resize(width);
        _tuckShade.Resize(width);
    }

    void Paint()
    {
        var theme = Board.Theme;
        var layers = Board.Layers;
        var plain = theme.IsHighContrast;
        // High contrast: a plain board outlined in the system's colours, no shades and no veil.
        _root.IsVisible = !plain;
        layers.Cork.Background = plain ? theme.Brush(theme.Canvas) : null;
        layers.Cork.BorderBrush = plain ? theme.Brush(theme.CanvasText) : null;
        layers.Cork.BorderThickness = plain ? new Thickness(1, 0, 1, 1) : new Thickness(0);
        layers.Veil.Visibility = plain ? Visibility.Collapsed : Visibility.Visible;
        layers.Veil.Background = theme.Brush(theme.Unlit);
        if (plain)
        {
            return;
        }
        var dark = theme.IsDark;
        var felt = theme.FeltTones;
        var finish = FrameFinishes.For(Board.Look.Frame).In(dark);
        _shadow.Paint(dark);
        _rails.Paint(finish, RailSheen.In(dark));
        _felt.Paint(felt);
        _leftCap.Paint(finish.Cap, CapShade.In(dark));
        _rightCap.Paint(finish.Cap, CapShade.In(dark));
        _innerShade.Paint(felt.InnerShade);
        _tuckShade.Paint(felt.TuckShade);
    }
}
