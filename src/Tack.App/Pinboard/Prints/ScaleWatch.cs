using Microsoft.UI.Xaml;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// Follows the screen scale an element is drawn at (physical px per logical
/// px), while it is in a window: each change is told once, on the UI thread.
/// </summary>
sealed class ScaleWatch
{
    readonly FrameworkElement _element;
    readonly Action<double> _changed;
    XamlRoot? _root;

    /// <param name="element">The element whose window is followed.</param>
    /// <param name="scale">The scale it is assumed to start at.</param>
    /// <param name="changed">Told each new scale.</param>
    public ScaleWatch(FrameworkElement element, double scale, Action<double> changed)
    {
        (_element, Scale, _changed) = (element, scale, changed);
        element.Loaded += (_, _) => Attach();
        element.Unloaded += (_, _) => Detach();
    }

    public double Scale { get; private set; }

    void Attach()
    {
        if (_element.XamlRoot is not { } root || root == _root)
        {
            return;
        }
        Detach();
        _root = root;
        root.Changed += Read;
        Read(root, null);
    }

    void Detach()
    {
        if (_root is not null)
        {
            _root.Changed -= Read;
            _root = null;
        }
    }

    void Read(XamlRoot root, XamlRootChangedEventArgs? e)
    {
        var scale = root.RasterizationScale;
        if (scale > 0 && scale != Scale)
        {
            Scale = scale;
            _changed(scale);
        }
    }
}
