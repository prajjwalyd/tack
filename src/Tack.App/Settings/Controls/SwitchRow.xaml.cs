using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Tack.App.Settings.Controls;

/// <summary>
/// A row of a card with an on/off switch, as Windows Settings has them. The
/// title names the switch for a screen reader and the description is its help
/// text. <see cref="Toggled"/> is raised only when the person flips it, never
/// when <see cref="IsOn"/> is set to show a setting.
/// </summary>
public sealed partial class SwitchRow : UserControl
{
    bool _showing;

    public SwitchRow()
    {
        InitializeComponent();
        Switch.Toggled += OnToggled;
    }

    /// <summary>The person flipped the switch: on or off.</summary>
    public event Action<bool>? Toggled;

    /// <summary>What the row is, as "Sound".</summary>
    public string Title
    {
        get => TitleText.Text;
        set
        {
            TitleText.Text = value;
            AutomationProperties.SetName(Switch, value);
        }
    }

    /// <summary>A line under the title; none if empty.</summary>
    public string Description
    {
        get => DescriptionText.Text;
        set
        {
            DescriptionText.Text = value;
            DescriptionText.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(Switch, value);
        }
    }

    /// <summary>Shows the switch on or off, without raising <see cref="Toggled"/>.</summary>
    public bool IsOn
    {
        get => Switch.IsOn;
        set
        {
            if (Switch.IsOn == value)
            {
                return;
            }
            _showing = true;
            Switch.IsOn = value;
            _showing = false;
        }
    }

    void OnToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            Toggled?.Invoke(Switch.IsOn);
        }
    }
}
