using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Phone;
using Tack.Core.Settings;

namespace Tack.App.Settings.Sections;

/// <summary>
/// Use on your phone: the card at the top of Settings. It says how the phone
/// link stands (<see cref="PhoneWords.Card"/>), with a dot by tone, and holds
/// one button that opens the phone page, where the switch, the code and the
/// allowed devices are. A change of its line is said aloud while the list shows.
/// </summary>
public sealed partial class PhoneCard : UserControl, ISettingsSection
{
    SettingsContext? _context;
    IPhoneLink _link = SavedPhoneLink.Instance;
    PhoneSettings? _saved;
    string? _shown;

    public PhoneCard()
    {
        InitializeComponent();
        Show(PhoneWords.Card(null));
    }

    public string Id => SettingsRoute.PhoneId;

    void ISettingsSection.Start(SettingsContext context)
    {
        _context = context;
        Open.Click += (_, _) => context.Go(SettingsRoute.PhoneId);
    }

    void ISettingsSection.Render(TackSettings settings)
    {
        _saved = settings.Phone;
        Refresh();
    }

    /// <summary>Shows what <paramref name="link"/> tells from now on, rather than the saved settings alone.</summary>
    internal void Use(IPhoneLink link)
    {
        _link = link;
        link.Changed += Refresh;
        Refresh();
    }

    void Refresh()
    {
        if (_saved is null)
        {
            return;
        }
        var card = PhoneWords.Card(_link.State(_saved));
        Show(card);
        // Said only while the list shows: the phone page speaks for itself.
        if (_shown is not null && _shown != card.Status && _context is { OnPhonePage: false })
        {
            _context.Announce(PhoneWords.CardAnnouncement(card));
        }
        _shown = card.Status;
    }

    void Show(PhoneCardView card)
    {
        Status.Text = card.Status;
        OkDot.Visibility = Seen(card.Tone == PhoneTone.Ok);
        AttentionDot.Visibility = Seen(card.Tone == PhoneTone.Attention);
        WarnDot.Visibility = Seen(card.Tone == PhoneTone.Warn);
        if (Open.Content as string != card.Button)
        {
            Open.Content = card.Button;
            Open.Style = card.Accent ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        }
        AutomationProperties.SetHelpText(Open, card.Status);
    }

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}
