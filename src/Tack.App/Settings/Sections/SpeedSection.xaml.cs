using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Reveal;
using Tack.Core.Settings;

namespace Tack.App.Settings.Sections;

/// <summary>
/// Speed: Instant, Normal or Slow, in that order. How long the pointer rests
/// at the edge is the edge poller's to read; the board scales its motion from
/// the look it is told.
/// </summary>
public sealed partial class SpeedSection : UserControl, ISettingsSection
{
    static readonly RevealSpeed[] s_speeds = [RevealSpeed.Instant, RevealSpeed.Normal, RevealSpeed.Slow];

    SettingsContext? _context;
    bool _showing;

    public SpeedSection()
    {
        InitializeComponent();
        AutomationProperties.SetHelpText(Speeds, Description.Text);
        Speeds.SelectionChanged += OnSelectionChanged;
    }

    public string Id => "speed";

    void ISettingsSection.Start(SettingsContext context) => _context = context;

    void ISettingsSection.Render(TackSettings settings)
    {
        var index = Array.IndexOf(s_speeds, settings.Reveal);
        if (Speeds.SelectedIndex != index)
        {
            _showing = true;
            Speeds.SelectedIndex = index;
            _showing = false;
        }
    }

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && Speeds.SelectedIndex is >= 0 and var index && index < s_speeds.Length)
        {
            _context?.Settings.SetReveal(s_speeds[index]);
        }
    }
}
