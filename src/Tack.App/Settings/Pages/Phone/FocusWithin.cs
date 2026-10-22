using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Tack.App.Settings.Pages.Phone;

/// <summary>Whether the keyboard focus is inside a part of the page, so a part that rebuilds knows to keep the keyboard nearby.</summary>
static class FocusWithin
{
    /// <summary>True when the focused element is <paramref name="part"/> or inside it.</summary>
    public static bool Has(UIElement part)
    {
        var node = part.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as DependencyObject : null;
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, part))
            {
                return true;
            }
        }
        return false;
    }
}
