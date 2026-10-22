using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Phone;

namespace Tack.App.Settings.Pages.Phone;

/// <summary>An allowed device in "Your devices": its name, "Allowed 3 hours ago", and Remove.</summary>
public sealed partial class DeviceRow : UserControl
{
    /// <param name="remove">Called with the device when Remove is pressed.</param>
    public DeviceRow(Device device, Action<Device> remove)
    {
        InitializeComponent();
        Device = device;
        NameText.Text = device.Name;
        AutomationProperties.SetName(Remove, $"Remove {device.Name}");
        Remove.Click += (_, _) => remove(device);
    }

    /// <summary>The device this row shows.</summary>
    internal Device Device { get; }

    /// <summary>Says how long ago it was allowed, as of <paramref name="nowMs"/>.</summary>
    public void ShowAge(long nowMs)
    {
        WhenText.Text = PhoneWords.Allowed(Device, nowMs);
        AutomationProperties.SetHelpText(Remove, WhenText.Text);
    }

    /// <summary>Moves the keyboard focus to Remove.</summary>
    public bool FocusRemove() => Remove.Focus(FocusState.Programmatic);
}
