using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Tack.App.Pinboard.Motion;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard;

/// <summary>
/// The board hangs from the screen edge on a hinge. Tucked, it is folded up
/// under the edge (-88 degrees about (50%, 24) in perspective, its unlit veil
/// shown); it swings down on the reveal spring, overshooting flat by a few
/// degrees as the veil lifts, and folds back up gathering speed. Under reduced
/// motion it slides instead, up out of sight and back, with no veil. Each move
/// starts from wherever the board is, so a reveal mid-fold turns straight back.
/// The compositor runs it all off the UI thread.
/// </summary>
sealed class Hinge
{
    /// <summary>Tucked under reduced motion: its own height and the hidden part above the screen.</summary>
    const double SlidUp = -(BoardMetrics.ElementHeight + BoardMetrics.TopHidden);

    readonly Keyframer _keyframer;
    readonly Visual _stage;
    readonly Visual _board;
    readonly Visual _veil;
    double _width = BoardMetrics.MinWidth;

    public Hinge(BoardLayers layers, Keyframer keyframer)
    {
        _keyframer = keyframer;
        _stage = ElementCompositionPreview.GetElementVisual(layers.Stage);
        ElementCompositionPreview.SetIsTranslationEnabled(layers.Board, true);
        _board = ElementCompositionPreview.GetElementVisual(layers.Board);
        _board.RotationAxis = Vector3.UnitX;
        _veil = ElementCompositionPreview.GetElementVisual(layers.Veil);
        Aim(BoardMetrics.MinWidth);
        Fold(reduced: false);
    }

    /// <summary>
    /// For a board <paramref name="width"/> wide: it turns about its top edge's
    /// middle at the screen edge, seen from in front of that point.
    /// </summary>
    public void Aim(double width)
    {
        _width = width;
        var middle = (float)(width / 2);
        _board.CenterPoint = new Vector3(middle, (float)BoardMetrics.TopHidden, 0);
        // The stage's own origin is the screen edge at the board's left, so the hinge is at (middle, 0) on it.
        var perspective = Matrix4x4.Identity;
        perspective.M34 = (float)(-1 / BoardMetrics.Perspective);
        var hinge = new Vector3(middle, 0, 0);
        _stage.TransformMatrix = Matrix4x4.CreateTranslation(-hinge) * perspective * Matrix4x4.CreateTranslation(hinge);
    }

    /// <summary>
    /// Off screen, the stage is drawn flat: a perspective makes the compositor
    /// keep a buffer to draw it into, which a hidden board does not need.
    /// </summary>
    public void Flatten() => _stage.TransformMatrix = Matrix4x4.Identity;

    /// <summary>Back in perspective, before the board is seen again.</summary>
    public void Unflatten() => Aim(_width);

    /// <summary>Tucked at once, ready to come down: folded up, or slid up under reduced motion.</summary>
    public void Fold(bool reduced)
    {
        Set(_board, Channel.Rotation, reduced ? 0 : BoardMetrics.HingeFrom);
        Set(_board, Channel.Y, reduced ? SlidUp : 0);
        Set(_veil, Channel.Opacity, reduced ? 0 : 1);
    }

    public void Open(MotionClock clock, bool reduced)
    {
        if (reduced)
        {
            Set(_board, Channel.Rotation, 0);
            Set(_veil, Channel.Opacity, 0);
            _keyframer.Transition(_board, Channel.Y, 0, clock.Scaled(BoardMetrics.ReducedRevealMs), CubicBezier.EaseOut);
            return;
        }
        Set(_board, Channel.Y, 0);
        var ms = clock.Scaled(Spring.Reveal.Ms);
        _keyframer.Transition(_board, Channel.Rotation, 0, ms, Spring.Reveal.Easing);
        _keyframer.Transition(_veil, Channel.Opacity, 0, ms, Spring.Reveal.Easing);
    }

    public void Close(MotionClock clock, bool reduced)
    {
        if (reduced)
        {
            _keyframer.Transition(_board, Channel.Y, SlidUp, clock.Scaled(BoardMetrics.ReducedTuckMs), CubicBezier.EaseIn);
            return;
        }
        var ms = clock.Scaled(BoardMetrics.TuckMs);
        _keyframer.Transition(_board, Channel.Rotation, BoardMetrics.HingeFrom, ms, CubicBezier.Tuck);
        _keyframer.Transition(_veil, Channel.Opacity, 1, ms, CubicBezier.Tuck);
    }

    void Set(Visual visual, Channel channel, double value) => _keyframer.Set(visual, channel, value);
}
