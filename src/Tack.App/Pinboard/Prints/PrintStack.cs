using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// Where a print's slot sits among its neighbours: a leaving print over an
/// arriving one, over the hovered one, over one lifted by a reorder, over the rest.
/// </summary>
sealed class PrintStack(UIElement slot)
{
    bool _arriving;
    bool _moving;
    bool _leaving;
    bool _hovered;

    public bool Arriving { get => _arriving; set => Restack(_arriving = value); }

    public bool Moving { get => _moving; set => Restack(_moving = value); }

    public bool Leaving { get => _leaving; set => Restack(_leaving = value); }

    public bool Hovered { get => _hovered; set => Restack(_hovered = value); }

    void Restack(bool _) => Canvas.SetZIndex(slot, _leaving ? 30 : _arriving ? 25 : _hovered ? 20 : _moving ? 18 : 1);
}
