using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tack.Core.Shortcuts;
using Windows.System;
using Windows.UI.Core;

namespace Tack.App.Settings.Sections;

/// <summary>
/// A shortcut field listening for the next chord typed, one field at a time.
/// Tack's own shortcuts are paused meanwhile, or pressing the current chord
/// would show the board instead of being heard. It hears every key before
/// anything else in the window (so Esc, Backspace and the rest never reach
/// the window's own keys); Tab, a click anywhere but the field, or the window
/// losing the focus or closing stops it, and the shortcuts always come back.
/// </summary>
sealed class ShortcutListener
{
    readonly SettingsContext _context;
    UIElement? _field;

    public ShortcutListener(SettingsContext context)
    {
        _context = context;
        context.Root.PreviewKeyDown += OnKeyDown;
        context.Root.PreviewKeyUp += OnKeyUp;
        context.Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        context.Leaving += End;
    }

    /// <summary>The row listening, or null.</summary>
    public int? Row { get; private set; }

    /// <summary>The modifiers held while listening changed (or a press was not a chord: none): show them.</summary>
    public event Action<int, IReadOnlyList<string>>? HeldChanged;

    /// <summary>A press that is not a usable chord.</summary>
    public event Action<int>? Hinted;

    /// <summary>A new chord for the row ("Win+Alt+S"; empty to turn it off), heard once the shortcuts are back.</summary>
    public event Action<int, string>? Picked;

    /// <summary>The row stopped listening.</summary>
    public event Action<int>? Ended;

    /// <summary>Starts listening for <paramref name="row"/>, whose field is <paramref name="field"/>; another row stops first.</summary>
    public void Start(int row, UIElement field)
    {
        if (Row == row)
        {
            return;
        }
        End();
        (Row, _field) = (row, field);
        _context.Settings.PauseShortcuts(true);
        HeldChanged?.Invoke(row, []);
    }

    /// <summary>Stops listening and gives Tack's shortcuts back.</summary>
    public void End()
    {
        if (Row is not { } row)
        {
            return;
        }
        (Row, _field) = (null, null);
        _context.Settings.PauseShortcuts(false);
        Ended?.Invoke(row);
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Row is not { } row)
        {
            return;
        }
        var step = ChordCapture.Interpret(Press(e.Key));
        if (step.Act == CaptureAct.Leave)
        {
            // The focus moves on as usual.
            End();
            return;
        }
        e.Handled = true;
        switch (step.Act)
        {
            case CaptureAct.Held:
                HeldChanged?.Invoke(row, step.Keys);
                break;
            case CaptureAct.Cancel:
                End();
                break;
            case CaptureAct.Off or CaptureAct.Chord:
                // Applying changes the shortcuts, so they are given back first.
                End();
                Picked?.Invoke(row, step.Chord);
                break;
            default:
                Hinted?.Invoke(row);
                HeldChanged?.Invoke(row, []);
                break;
        }
    }

    void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (Row is { } row)
        {
            e.Handled = true;
            HeldChanged?.Invoke(row, ChordCapture.HeldAfterRelease(Press(e.Key)));
        }
    }

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_field is not null && !Within(e.OriginalSource as DependencyObject, _field))
        {
            End();
        }
    }

    static bool Within(DependencyObject? element, UIElement field)
    {
        for (var at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, field))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The key and the modifiers held as Windows has them for this thread now.</summary>
    static KeyPress Press(VirtualKey key) => new(
        (ushort)key,
        Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows),
        Down(VirtualKey.Control),
        Down(VirtualKey.Menu),
        Down(VirtualKey.Shift));

    static bool Down(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
