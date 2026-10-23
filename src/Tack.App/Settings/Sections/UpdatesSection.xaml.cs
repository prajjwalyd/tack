using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Settings;
using Tack.Core.Updates;

namespace Tack.App.Settings.Sections;

/// <summary>
/// Updates: this version of Tack, the "check when Tack starts" switch, and a
/// row saying how updates stand (<see cref="UpdateWords"/>) with the one
/// button that fits: check, install, or nothing while it works. The release
/// notes are the update server's text, so they are only ever shown as text.
/// Until an updater is handed over (<see cref="Use"/>) the row shows "Check
/// for updates" with its button off.
/// </summary>
public sealed partial class UpdatesSection : UserControl, ISettingsSection
{
    SettingsContext? _context;
    IUpdater? _updater;
    UpdateStatus _status = UpdateStatus.Idle;
    UpdatePhase? _shown;

    public UpdatesSection()
    {
        InitializeComponent();
        ActionButton.Click += OnAction;
    }

    public string Id => "updates";

    void ISettingsSection.Start(SettingsContext context)
    {
        _context = context;
        VersionText.Text = $"Version {context.Version}";
        CheckSwitch.Toggled += context.Settings.SetUpdatesCheck;
        Show(_status);
    }

    /// <summary>
    /// Shows how <paramref name="updater"/> stands, and lets the button check
    /// and install with it. The window, which hears it change, calls
    /// <see cref="ShowStatus"/> each time.
    /// </summary>
    internal void Use(IUpdater updater)
    {
        _updater = updater;
        Show(updater.Status);
    }

    /// <summary>The updater's status changed: show it as it stands now.</summary>
    internal void ShowStatus()
    {
        if (_updater is { } updater)
        {
            Show(updater.Status);
        }
    }

    void ISettingsSection.Render(TackSettings settings)
    {
        CheckSwitch.IsOn = settings.Updates.Check;
        // "Last checked 5 minutes ago" is worked out again whenever the window renders.
        Show(_status);
    }

    void Show(UpdateStatus status)
    {
        _status = status;
        var view = UpdateWords.Describe(status, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Headline.Text = view.Title;
        Sub.Text = view.Sub;
        Sub.Visibility = view.Sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowButton(view.Button);
        ShowMeter(view.Meter);
        ShowNotes(status);

        if (_shown is { } shown && shown != status.State)
        {
            _context?.Announce(UpdateWords.Announcement(status, view));
        }
        _shown = status.State;
    }

    void ShowButton(UpdateButton button)
    {
        ActionButton.Content = button.Label;
        ActionButton.Style = button.Accent ? XamlStyles.App("AccentButtonStyle") : null;
        // Busy, it stays enabled so it keeps the focus; it only looks quiet and does nothing.
        ActionButton.IsEnabled = _updater is not null;
        ActionButton.Opacity = button.Busy ? 0.55 : 1;
        AutomationProperties.SetHelpText(ActionButton, Sub.Text);
    }

    void ShowMeter(UpdateMeter? meter)
    {
        Meter.Visibility = meter is null ? Visibility.Collapsed : Visibility.Visible;
        Meter.IsIndeterminate = meter is { Fraction: null };
        Meter.Value = meter?.Fraction is { } fraction ? fraction * 100 : 0;
    }

    void ShowNotes(UpdateStatus status)
    {
        var text = status.Notes?.Trim() ?? string.Empty;
        var shown = text.Length > 0 && !string.IsNullOrEmpty(status.Version);
        Notes.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        NotesTitle.Text = shown ? $"What's new in {status.Version}" : string.Empty;
        AutomationProperties.SetName(Notes, NotesTitle.Text);
        if (NotesText.Text != text)
        {
            NotesText.Text = text;
        }
    }

    /// <summary>Check or install, as the button says; the next phase shows at once and the answer settles it.</summary>
    async void OnAction(object sender, RoutedEventArgs e)
    {
        if (_updater is not { } updater || _status.Busy)
        {
            return;
        }
        var before = _status;
        var act = UpdateWords.Describe(before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Button.Act;
        UpdateStatus? after;
        string failure;
        if (act == UpdateAction.Check)
        {
            Show(before with { State = UpdatePhase.Checking, Error = null });
            after = await updater.CheckAsync();
            failure = UpdateWords.CheckFailed;
        }
        else if (act == UpdateAction.Install)
        {
            Show(before with { State = UpdatePhase.Downloading, Progress = null });
            after = await updater.InstallAsync();
            failure = UpdateWords.InstallFailed;
        }
        else
        {
            return;
        }
        Show(after ?? before);
        if (after is null)
        {
            _context?.Announce(failure);
        }
    }
}
