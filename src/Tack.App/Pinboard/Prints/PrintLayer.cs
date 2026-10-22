using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// One named layer of a print: its element, and the element's compositor
/// visual for motions (translation enabled, so Translation.X and .Y can move).
/// </summary>
sealed class PrintLayer
{
    public PrintLayer(string name, FrameworkElement element)
    {
        (Name, Element) = (name, element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual = ElementCompositionPreview.GetElementVisual(element);
    }

    /// <summary>For traces and tests: "slot", "drop", "card"...</summary>
    public string Name { get; }

    public FrameworkElement Element { get; }

    public Visual Visual { get; }
}
