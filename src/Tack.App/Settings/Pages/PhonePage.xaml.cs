using Microsoft.UI.Xaml.Controls;
using Tack.Core.Phone;
using Tack.Core.Settings;

namespace Tack.App.Settings.Pages;

/// <summary>
/// The phone page: the phone card's button, the tray's "Use on your phone…"
/// and a device asking to use the board all land here. Everything renders from
/// one <see cref="PhoneState"/>: the requests at the top, the switch, the body
/// and the allowed devices, each changed in place so the focus is never pulled
/// away. Until a phone link is handed over (<see cref="Use"/>) the state is the
/// saved settings alone, so the switch and the devices work and the body says
/// "Getting ready…".
/// </summary>
public sealed partial class PhonePage : UserControl, ISettingsSection
{
    SettingsContext? _context;
    IPhoneLink _link = SavedPhoneLink.Instance;
    PhoneSettings? _saved;
    bool? _serving;

    public PhonePage()
    {
        InitializeComponent();
        // A request sits first: when one arrives, the page shows its top.
        Asks.Arrived += () => (Parent as ScrollViewer)?.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>Not a group of the list: the window shows it for <see cref="SettingsRoute.Phone"/>.</summary>
    public string Id => "phone-page";

    void ISettingsSection.Start(SettingsContext context)
    {
        _context = context;
        OnSwitch.Toggled += context.Settings.SetPhoneOn;
        Asks.Start(context);
        Body.Start(context);
        Devices.Start(context);
    }

    void ISettingsSection.Render(TackSettings settings)
    {
        _saved = settings.Phone;
        Refresh();
    }

    /// <summary>
    /// Shows what <paramref name="link"/> tells from now on, and answers requests
    /// through it. The window renders again on its changes.
    /// </summary>
    internal void Use(IPhoneLink link)
    {
        _link = link;
        Asks.Use(link);
        Refresh();
    }

    void Refresh()
    {
        if (_saved is null)
        {
            return;
        }
        var state = _link.State(_saved);
        OnSwitch.IsOn = state.On;
        Asks.Render(state.Pending);
        Body.Render(PhoneWords.Body(state));
        Devices.Render(state.Devices);
        if (_serving == false && state.Serving && _context is { OnPhonePage: true })
        {
            _context.Announce(PhoneWords.Ready);
        }
        _serving = state.Serving;
    }
}
