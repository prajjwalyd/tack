using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Tack.Core.Appearance;
using Windows.UI;

namespace Tack.App.Settings.Sections.Appearance;

/// <summary>
/// The board colour's custom colour picker, in a flyout under the Custom
/// swatch. A colour dragged about is sent at most once a
/// <see cref="ColourDrag.Beat"/>, and one still waiting when it closes is sent
/// then, so the board follows the drag without saving at every step.
/// </summary>
sealed class FeltPicker
{
    readonly ColorPicker _picker = new()
    {
        IsAlphaEnabled = false,
        IsMoreButtonVisible = false,
        IsColorChannelTextInputVisible = false,
        IsHexInputVisible = true,
        IsColorPreviewVisible = true,
    };

    readonly Flyout _flyout;
    readonly ColourDrag _drag = new();
    readonly DispatcherQueueTimer _beat;
    bool _showing;

    public FeltPicker(DispatcherQueue ui)
    {
        AutomationProperties.SetName(_picker, "Custom colour");
        // The window can be short, sitting under the board: the picker may reach past it.
        _flyout = new Flyout { Content = _picker, Placement = FlyoutPlacementMode.Bottom, ShouldConstrainToRootBounds = false };
        _beat = ui.CreateTimer();
        _beat.Interval = ColourDrag.Beat;
        _beat.IsRepeating = false;
        _beat.Tick += (_, _) => Send(_drag.Due());
        _picker.ColorChanged += OnColorChanged;
        _flyout.Closed += OnClosed;
    }

    /// <summary>A colour to set the board to, from the picker.</summary>
    public event Action<Felt.CustomFelt>? Picked;

    /// <summary>Opens the picker on <paramref name="start"/>, under <paramref name="at"/>.</summary>
    public void Open(FrameworkElement at, Felt.CustomFelt start)
    {
        _drag.Open(start);
        _showing = true;
        _picker.Color = Color.FromArgb(255, start.R, start.G, start.B);
        _showing = false;
        _flyout.ShowAt(at);
    }

    void OnColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        var colour = args.NewColor;
        if (!_showing && _drag.Move(new Felt.CustomFelt(colour.R, colour.G, colour.B)))
        {
            _beat.Start();
        }
    }

    void OnClosed(object? sender, object e)
    {
        _beat.Stop();
        Send(_drag.Close());
    }

    void Send(Felt.CustomFelt? colour)
    {
        if (colour is not null)
        {
            Picked?.Invoke(colour);
        }
    }
}
