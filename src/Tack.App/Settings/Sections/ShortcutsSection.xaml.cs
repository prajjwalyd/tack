using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Settings;
using Tack.Core.Shortcuts;

namespace Tack.App.Settings.Sections;

/// <summary>
/// Shortcuts: show or hide the board, and pin the selection. Choosing a field
/// (a click, Enter or Space) listens for the next chord (see
/// <see cref="ShortcutListener"/>): Esc cancels, Backspace or Delete turns the
/// shortcut off, Tab moves on. A chord that is free is applied at once and
/// shown; one another app holds, one the other shortcut has, or one that is
/// not usable is reported under its row, and the old chord stays.
/// </summary>
public sealed partial class ShortcutsSection : UserControl, ISettingsSection
{
    const int Toggle = 0, Pin = 1;

    readonly ShortcutRow[] _rows;
    SettingsContext? _context;
    ShortcutListener? _listener;
    IReadOnlyList<string> _held = [];

    /// <summary>The chords applied, "Win+Alt+S" or empty for off: show or hide, then pin.</summary>
    readonly string[] _chords = ["", ""];

    public ShortcutsSection()
    {
        InitializeComponent();
        _rows =
        [
            new("Show or hide the board", "to show or hide the board", ToggleField, ToggleRing, ToggleStatus, ToggleStatusLine),
            new("Pin the selection", "to pin the selection", PinField, PinRing, PinStatus, PinStatusLine),
        ];
        AutomationProperties.SetName(this, "Shortcuts");
        AutomationProperties.SetHelpText(this, Intro.Text);
    }

    public string Id => "shortcuts";

    void ISettingsSection.Start(SettingsContext context)
    {
        _context = context;
        _listener = new ShortcutListener(context);
        _listener.HeldChanged += (row, keys) =>
        {
            _held = keys;
            Show(row);
        };
        _listener.Hinted += row => SetStatus(row, ChordCapture.Hint);
        _listener.Picked += ChangeRow;
        _listener.Ended += row =>
        {
            _held = [];
            Show(row);
        };
        for (var i = 0; i < _rows.Length; i++)
        {
            var row = i;
            _rows[i].Field.Click += (_, _) => StartListening(row);
        }
        Reset.Click += OnReset;
        var current = context.Settings.Shortcuts;
        SetStatus(Toggle, ShortcutState.Describe(current.Toggle));
        SetStatus(Pin, ShortcutState.Describe(current.Pin));
    }

    void ISettingsSection.Render(TackSettings settings)
    {
        if (_context is null)
        {
            return;
        }
        var current = _context.Settings.Shortcuts;
        (_chords[Toggle], _chords[Pin]) = (current.Toggle.Chord, current.Pin.Chord);
        ShowAll();
    }

    void StartListening(int row)
    {
        SetStatus(row, "");
        _listener?.Start(row, _rows[row].Field);
    }

    /// <summary>Applies one row's new chord ("" for off), and says so if it took.</summary>
    void ChangeRow(int row, string chord)
    {
        var next = (string[])_chords.Clone();
        next[row] = chord;
        if (Apply(next[Toggle], next[Pin], row))
        {
            _context?.Announce(chord.Length > 0 ? $"{_rows[row].Label}: {ChordCapture.Speak(chord)}" : $"{_rows[row].Label}: turned off");
        }
        ShowAll();
    }

    void OnReset(object sender, RoutedEventArgs e)
    {
        _listener?.End();
        if (Apply(Chord.DefaultToggle, Chord.DefaultPin, row: null))
        {
            _context?.Announce("Shortcuts reset");
        }
        ShowAll();
    }

    /// <summary>
    /// Asks for both chords. If both are free they are applied and kept here;
    /// otherwise nothing changes and each row that is the trouble says so.
    /// <paramref name="row"/> is the row being changed (null: both, a reset).
    /// </summary>
    bool Apply(string toggle, string pin, int? row)
    {
        SetStatus(Toggle, "");
        SetStatus(Pin, "");
        if (row is { } changed && toggle.Length > 0 && toggle == pin)
        {
            SetStatus(changed, $"Already used {_rows[1 - changed].Use}");
            return false;
        }
        if (_context is null)
        {
            return false;
        }
        var result = _context.Settings.SetShortcuts(toggle, pin);
        if (!result.AllWork)
        {
            SetStatus(Toggle, ShortcutState.Describe(result.Toggle));
            SetStatus(Pin, ShortcutState.Describe(result.Pin));
            return false;
        }
        (_chords[Toggle], _chords[Pin]) = (result.Toggle.Chord, result.Pin.Chord);
        return true;
    }

    void ShowAll()
    {
        Show(Toggle);
        Show(Pin);
        Reset.IsEnabled = _chords[Toggle] != Chord.DefaultToggle || _chords[Pin] != Chord.DefaultPin;
    }

    void Show(int row)
    {
        var parts = _rows[row];
        var listening = _listener?.Row == row;
        parts.Ring.Style = (Style)Application.Current.Resources[listening ? "SettingsFieldRingListening" : "SettingsFieldRing"];
        var chord = _chords[row];
        Keycaps.Fill(parts.Field, listening ? _held : chord.Length > 0 ? chord.Split('+') : [], listening);
        AutomationProperties.SetName(parts.Field, listening
            ? $"{parts.Label}: press a shortcut. Escape cancels, Backspace turns it off."
            : $"{parts.Label}: {ChordCapture.Speak(chord)}. Press Enter to change.");
    }

    /// <summary>The line under a row: what is wrong, said to a screen reader too; nothing hides it.</summary>
    void SetStatus(int row, string text)
    {
        var parts = _rows[row];
        parts.Status.Text = text;
        parts.StatusLine.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(parts.Field, text);
        if (text.Length > 0)
        {
            _context?.Announce(text);
        }
    }
}
