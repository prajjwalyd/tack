using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Tack.App.Pinboard.Motion;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// The cork growing or shrinking to a new width: the board takes its new
/// width at once, and the cork is drawn squeezed or stretched back to the old
/// one, about its top centre, then eased to its own size on the layout spring.
/// So the layout itself is never animated, only the picture of it.
/// </summary>
sealed class CorkSlide(BoardLayers layers, Keyframer keyframer)
{
    readonly Visual _cork = ElementCompositionPreview.GetElementVisual(layers.Cork);
    bool _slid;

    /// <summary>The board went from <paramref name="from"/> to <paramref name="to"/> wide: slides over <paramref name="ms"/>, or settles at once for null.</summary>
    public void Resize(double from, double to, double? ms)
    {
        _cork.CenterPoint = new Vector3((float)(to / 2), 0, 0);
        if (ms is not { } duration || from <= 0 || to <= 0)
        {
            Settle();
            return;
        }
        var motion = new Timeline(duration)
            .Key(0, new Pose { ScaleX = from / to }, Spring.Layout.Easing)
            .Key(1, new Pose { ScaleX = 1 })
            .Sample();
        keyframer.Play(motion, _cork);
        _slid = true;
    }

    /// <summary>The cork at its own size now, if a slide may have left it otherwise (a tuck, an instant layout).</summary>
    public void Settle()
    {
        if (_slid)
        {
            keyframer.Set(_cork, Channel.ScaleX, 1);
            _slid = false;
        }
    }
}
