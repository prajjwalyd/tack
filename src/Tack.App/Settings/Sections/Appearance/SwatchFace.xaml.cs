using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Tack.App.Settings.Sections.Appearance;

/// <summary>
/// What one swatch shows inside its button: a picture, round or a little
/// square note, with a hairline edge; when it is the one chosen, a ring 2 px
/// out, a check mark and its name in the primary ink.
/// </summary>
public sealed partial class SwatchFace : UserControl
{
    bool _chosen;

    public SwatchFace()
    {
        InitializeComponent();
        // A state set before the face is in the tree is set again once it is.
        Loaded += (_, _) => ShowChoice();
    }

    /// <summary>The name under the picture.</summary>
    public string Text
    {
        get => Label.Text;
        set => Label.Text = value;
    }

    /// <summary>A square picture with rounded corners (a note) rather than a round one.</summary>
    public bool Square
    {
        set
        {
            Ring.CornerRadius = new CornerRadius(value ? 8 : 20);
            Edge.CornerRadius = new CornerRadius(value ? 5 : 17);
        }
    }

    /// <summary>Whether this is the swatch chosen: the ring, the check mark and the primary ink.</summary>
    public bool Chosen
    {
        get => _chosen;
        set
        {
            _chosen = value;
            ShowChoice();
        }
    }

    /// <summary>Shows <paramref name="picture"/> (32 px) with a check mark in <paramref name="ink"/>, haloed in <paramref name="glow"/>.</summary>
    public void Paint(UIElement picture, Color ink, Color glow)
    {
        Picture.Children.Clear();
        Picture.Children.Add(picture);
        Check.Stroke = new SolidColorBrush(ink);
        Glow.Stroke = new SolidColorBrush(glow);
    }

    void ShowChoice() => VisualStateManager.GoToState(this, _chosen ? "ChosenState" : "UnchosenState", false);
}
