using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tack.Core.Appearance;
using Windows.System;
using Windows.UI.Core;

namespace Tack.App.Settings.Sections.Appearance;

/// <summary>
/// An Appearance card: a title, the chosen swatch's name at the right, and
/// the swatches as one radio group. The group is one tab stop (the swatch
/// chosen, or the first); the arrow keys, Home and End move to a swatch and
/// choose it, except a manual one (Custom), which they only move to and Enter
/// or Space opens. <see cref="Picked"/> is raised only for the person's choice,
/// never when <see cref="Show"/> shows a setting.
/// </summary>
public sealed partial class SwatchCard : UserControl
{
    /// <summary>How wide a swatch's button is: its 40 px ring and room for a name like "Burgundy".</summary>
    const double SwatchWidth = 50;

    readonly List<RadioButton> _buttons = [];
    readonly List<SwatchFace> _faces = [];
    IReadOnlyList<SwatchChoice> _choices = [];
    string _chosen = "";
    bool _showing;

    public SwatchCard()
    {
        InitializeComponent();
        Swatches.PreviewKeyDown += OnKeyDown;
        Swatches.GotFocus += (_, _) => ShowTabStop();
        // Where the focus went is known only once it has moved.
        Swatches.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(ShowTabStop);
    }

    /// <summary>The person chose a swatch: its value. For a manual one this asks for its picker; nothing is chosen yet.</summary>
    public event Action<string>? Picked;

    /// <summary>The card's title, as "Board colour".</summary>
    public string Heading
    {
        get => Title.Text;
        set => Title.Text = value;
    }

    /// <summary>The swatches' faces, in the order of the choices, for painting their pictures.</summary>
    public IReadOnlyList<SwatchFace> Faces => _faces;

    /// <summary>Makes a swatch for each of <paramref name="choices"/>, in the shared grid of nine columns. Once.</summary>
    public void Build(IReadOnlyList<SwatchChoice> choices, bool square)
    {
        _choices = choices;
        for (var column = 0; column < FeltChoices.Columns; column++)
        {
            if (column > 0)
            {
                Swatches.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            Swatches.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SwatchWidth) });
        }
        for (var n = 0; n < choices.Count; n++)
        {
            var face = new SwatchFace { Text = choices[n].Name, Square = square };
            var button = new RadioButton { Style = XamlStyles.In(Resources, "SwatchButton"), Content = face, IsTabStop = n == 0 };
            AutomationProperties.SetName(button, choices[n].Label);
            AutomationProperties.SetPositionInSet(button, n + 1);
            AutomationProperties.SetSizeOfSet(button, choices.Count);
            AutomationProperties.SetHelpText(button, Title.Text);
            Grid.SetColumn(button, n * 2);
            var index = n;
            // Checked covers a click, Space and a screen reader's select alike; a click on the
            // picker's swatch while it is already chosen checks nothing, so it opens the picker itself.
            button.Checked += (_, _) =>
            {
                if (!_showing)
                {
                    Choose(index);
                }
            };
            button.Click += (_, _) =>
            {
                if (_choices[index].Manual && button.IsChecked == true)
                {
                    Choose(index);
                }
            };
            Swatches.Children.Add(button);
            _buttons.Add(button);
            _faces.Add(face);
        }
    }

    /// <summary>Shows <paramref name="value"/> as chosen and <paramref name="name"/> at the right of the title.</summary>
    public void Show(string value, string name)
    {
        _chosen = value;
        Value.Text = name;
        _showing = true;
        for (var n = 0; n < _buttons.Count; n++)
        {
            var chosen = _choices[n].Value == value;
            _buttons[n].IsChecked = chosen;
            _faces[n].Chosen = chosen;
        }
        _showing = false;
        ShowTabStop();
    }

    /// <summary>The button of the swatch whose value is <paramref name="value"/>: where a picker opens from.</summary>
    public FrameworkElement? ButtonOf(string value) => IndexOf(value) is >= 0 and var n ? _buttons[n] : null;

    /// <summary>Moves the keyboard focus to the group's tab stop.</summary>
    public void FocusGroup() => _buttons.Find(b => b.IsTabStop)?.Focus(FocusState.Keyboard);

    void Choose(int index)
    {
        var choice = _choices[index];
        if (choice.Manual || choice.Value != _chosen)
        {
            Picked?.Invoke(choice.Value);
        }
        // A radio checks itself; what is shown is what is set, whatever came of it. Put right once
        // the radio has finished checking itself and its group.
        DispatcherQueue.TryEnqueue(() => Show(_chosen, Value.Text));
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var at = FocusedIndex();
        if (at < 0 || Down(VirtualKey.Control) || Down(VirtualKey.Menu) || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows))
        {
            return;
        }
        var count = _buttons.Count;
        var to = e.Key switch
        {
            VirtualKey.Left or VirtualKey.Up => (at + count - 1) % count,
            VirtualKey.Right or VirtualKey.Down => (at + 1) % count,
            VirtualKey.Home => 0,
            VirtualKey.End => count - 1,
            _ => -1,
        };
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Choose(at);
            return;
        }
        if (to < 0)
        {
            return;
        }
        e.Handled = true;
        _buttons[to].IsTabStop = true;
        _buttons[to].Focus(FocusState.Keyboard);
        if (!_choices[to].Manual)
        {
            Choose(to);
        }
    }

    /// <summary>The one tab stop: the swatch with the focus if one has it, else the one chosen, else the first.</summary>
    void ShowTabStop()
    {
        var stop = FocusedIndex();
        if (stop < 0)
        {
            stop = Math.Max(0, IndexOf(_chosen));
        }
        for (var n = 0; n < _buttons.Count; n++)
        {
            _buttons[n].IsTabStop = n == stop;
        }
    }

    int FocusedIndex() =>
        XamlRoot is { } root && FocusManager.GetFocusedElement(root) is RadioButton focused ? _buttons.IndexOf(focused) : -1;

    int IndexOf(string value)
    {
        for (var n = 0; n < _choices.Count; n++)
        {
            if (_choices[n].Value == value)
            {
                return n;
            }
        }
        return -1;
    }

    static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
