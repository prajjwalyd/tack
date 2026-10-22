using Microsoft.UI.Xaml.Controls;
using Tack.Core.Settings;

namespace Tack.App.Settings.Sections;

/// <summary>
/// General: the switches the tray's menu has too (Sound, Reveal at top edge,
/// Start with Windows). A flip applies at once; there is no Save.
/// </summary>
public sealed partial class GeneralSection : UserControl, ISettingsSection
{
    SettingsContext? _context;

    public GeneralSection() => InitializeComponent();

    public string Id => "general";

    void ISettingsSection.Start(SettingsContext context)
    {
        _context = context;
        Sound.Toggled += context.Settings.SetSound;
        EdgeReveal.Toggled += context.Settings.SetEdgeReveal;
        Autostart.Toggled += context.Settings.SetAutostart;
    }

    void ISettingsSection.Render(TackSettings settings)
    {
        Sound.IsOn = settings.Sound;
        EdgeReveal.IsOn = settings.EdgeReveal;
        // Read from the Run key, not from board.json.
        Autostart.IsOn = _context?.Settings.Autostart ?? false;
    }
}
