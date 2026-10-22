using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Tack.Core.Phone;

namespace Tack.App.Settings.Pages.Phone;

/// <summary>
/// "Your devices": the devices allowed to use the board, each with Remove,
/// which forgets it (it has to ask again). The rows are rebuilt only when the
/// list changes; when the focused Remove goes away the focus goes to the next
/// one, else to the top of the page.
/// </summary>
public sealed partial class DeviceList : UserControl
{
    SettingsContext? _context;
    IReadOnlyList<Device>? _shown;

    public DeviceList() => InitializeComponent();

    /// <summary>Once, with the window's context.</summary>
    internal void Start(SettingsContext context) => _context = context;

    /// <summary>Shows <paramref name="devices"/>, and how long ago each was allowed as of now.</summary>
    public void Render(IReadOnlyList<Device> devices)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_shown is null || !_shown.SequenceEqual(devices))
        {
            Rebuild(devices);
        }
        foreach (var row in Rows.Children.OfType<DeviceRow>())
        {
            row.ShowAge(now);
        }
    }

    void Rebuild(IReadOnlyList<Device> devices)
    {
        var focused = FocusWithin.Has(this) ? FocusedIndex() : -1;
        _shown = [.. devices];
        Rows.Children.Clear();
        foreach (var device in devices)
        {
            if (Rows.Children.Count > 0)
            {
                Rows.Children.Add(new Rectangle { Style = (Style)Application.Current.Resources["SettingsDivider"] });
            }
            Rows.Children.Add(new DeviceRow(device, Forget));
        }
        Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (focused < 0)
        {
            return;
        }
        // The Remove that had the focus is gone: keep the keyboard nearby.
        var rows = Rows.Children.OfType<DeviceRow>().ToList();
        if (rows.Count > 0)
        {
            rows[Math.Min(focused, rows.Count - 1)].FocusRemove();
        }
        else
        {
            _context?.FocusTop();
        }
    }

    /// <summary>Which row holds the focus, counting rows only; -1 if none does.</summary>
    int FocusedIndex()
    {
        var rows = Rows.Children.OfType<DeviceRow>().ToList();
        return rows.FindIndex(FocusWithin.Has);
    }

    void Forget(Device device)
    {
        if (_context is not { } context)
        {
            return;
        }
        context.Settings.ForgetDevice(device.Key);
        context.Announce(PhoneWords.Removed(device.Name));
    }
}
