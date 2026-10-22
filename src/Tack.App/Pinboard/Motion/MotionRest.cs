using Tack.App.Pinboard.Model;
using Tack.App.Pinboard.Prints;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// Puts every layer the print motions move back at rest, once a tuck has
/// cancelled the motions where they were: nothing is left half arrived,
/// half pressed or mid-swing for the next reveal.
/// </summary>
static class MotionRest
{
    public static void Put(BoardItem item, Keyframer keyframer)
    {
        var view = item.View;
        view.Arriving = false;
        foreach (var layer in new[] { view.Drop, view.Pin })
        {
            Set(layer, Channel.Opacity, 1);
            Set(layer, Channel.X, 0);
            Set(layer, Channel.Y, 0);
            Set(layer, Channel.Scale, 1);
        }
        Set(view.Drop, Channel.Rotation, 0);
        Set(view.Swing, Channel.Rotation, 0);
        Set(view.PinShadow, Channel.Opacity, PrintView.PinShadowOpacity);
        Set(view.PinShadow, Channel.Scale, 1);
        Set(view.Glint, Channel.Opacity, 0);
        Set(view.Glint, Channel.X, -PrintView.GlintTravel);
        Set(view.Badge, Channel.Opacity, 0);
        Set(view.Badge, Channel.Scale, 1);
        Set(view.Shade, Channel.Opacity, 0);
        Set(view.Shade, Channel.Y, 0);
        Set(view.Shade, Channel.Scale, 1);
        Set(view.Picture, Channel.Opacity, 1);
        Set(view.Paper, Channel.Opacity, 1);
        // A keep press cut short flips its look now.
        view.SetKept(item.Print.Kept, animate: false);

        void Set(PrintLayer layer, Channel channel, double value) => keyframer.Set(layer.Visual, channel, value);
    }
}
