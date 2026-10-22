using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Tack.App.Pinboard.Chrome;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Look;
using Windows.UI;
using Windows.UI.ViewManagement;
using FeltSetting = Tack.Core.Appearance.Felt;
using PaperSetting = Tack.Core.Appearance.NotePaper;

namespace Tack.App.Pinboard;

/// <summary>
/// The board's colours for the Windows app theme (light or dark), for high
/// contrast, and for the felt and note paper the user picked (through
/// <see cref="Follow"/>), followed live. <see cref="Changed"/> fires on the UI
/// thread whenever any of them changes. The frame is the chrome's own business.
/// </summary>
sealed class BoardTheme
{
    readonly FrameworkElement _root;
    readonly DispatcherQueue _queue;
    readonly AccessibilitySettings _accessibility = new();
    readonly UISettings _settings = new();
    readonly Dictionary<Color, SolidColorBrush> _brushes = [];
    FeltSetting _felt = FeltSetting.Default;
    PaperSetting _paper = PaperSetting.Default;
    PaperTones _note = PaperTones.For(PaperSetting.Default, dark: false);
    HintTones _hint = HintTones.For(PaperSetting.Default, dark: false);

    public BoardTheme(FrameworkElement root, DispatcherQueue queue)
    {
        (_root, _queue) = (root, queue);
        root.ActualThemeChanged += (_, _) => Refresh();
        // Raised on another thread, for high contrast turning on or off too: the
        // accessibility settings' own event needs a core window, which a desktop app has not.
        _settings.ColorValuesChanged += (_, _) => _queue.TryEnqueue(() => Refresh());
        Read();
    }

    public event Action? Changed;

    public bool IsDark { get; private set; }

    public bool IsHighContrast { get; private set; }

    /// <summary>Windows' accent colour, which an "accent" felt is made from.</summary>
    public Color Accent { get; private set; }

    /// <summary>The user's felt, and the light and shade over it, in this theme.</summary>
    public FeltTones FeltTones { get; private set; } = FeltTones.For(FeltSetting.Default, default, dark: false);

    public Color Felt => FeltTones.Felt.ToColor();

    /// <summary>The paper of the notes, in the user's colour.</summary>
    public Color NotePaper => _note.Paper.ToColor();

    /// <summary>A link note's text, a blue that reads on the note paper.</summary>
    public Color NoteLink => _note.Link.ToColor();

    /// <summary>A link note's domain: the link a little softer.</summary>
    public Color NoteDomain => _note.Domain.ToColor();

    /// <summary>The empty board's card and the tip, cut from the note paper.</summary>
    public Color HintPaper => _hint.Paper.ToColor();

    /// <summary>The hint's sentence.</summary>
    public Color HintInk => _hint.Ink.ToColor();

    /// <summary>What the hint stresses: the keys, the setting's path.</summary>
    public Color HintInkStrong => _hint.InkStrong.ToColor();

    public Color Paper => IsDark ? Rgb(0xeb, 0xe8, 0xe2) : Rgb(0xfb, 0xfa, 0xf7);

    /// <summary>A photo with no picture yet.</summary>
    public Color PhotoEmpty => IsDark ? Rgb(0xb9, 0xb4, 0xab) : Rgb(0xdc, 0xd8, 0xd0);

    /// <summary>Light text on the felt: the age under a print.</summary>
    public Color CaptionInk => IsDark ? Rgba(226, 240, 231, 0.80) : Rgba(236, 246, 239, 0.86);

    public Color NoteInk => PaperTones.Ink.ToColor();

    /// <summary>The board facing away from the light while folded up: the felt's own shadow.</summary>
    public Color Unlit => FeltTones.Unlit.ToColor();

    public Color FocusRing => Rgba(255, 255, 255, 0.96);

    /// <summary>A kept print's pin.</summary>
    public Color Brass => Rgb(0xf2, 0xc2, 0x4b);

    /// <summary>The system colours high contrast draws with.</summary>
    public Color Canvas { get; private set; }

    public Color CanvasText { get; private set; }

    public Color Highlight { get; private set; }

    public Color HighlightText { get; private set; }

    public Color ButtonFace { get; private set; }

    public Color ButtonText { get; private set; }

    public Color LinkText { get; private set; }

    /// <summary>A pin head's body colour; the same in both themes.</summary>
    public static Color Pin(PinColor color) => color switch
    {
        PinColor.Terracotta => Rgb(0xf0, 0x39, 0x2b),
        PinColor.Sage => Rgb(0x2f, 0x6d, 0xf6),
        PinColor.Ink => Rgb(0x8a, 0x4c, 0xf0),
        PinColor.Sand => Rgb(0xfb, 0xe9, 0x4a),
        _ => Rgb(0xfb, 0xfb, 0xf8),
    };

    /// <summary>A pin head's shaded side.</summary>
    public static Color PinShade(PinColor color) => color switch
    {
        PinColor.Terracotta => Rgb(0xa3, 0x17, 0x0f),
        PinColor.Sage => Rgb(0x16, 0x3c, 0x9e),
        PinColor.Ink => Rgb(0x4f, 0x1f, 0xa3),
        PinColor.Sand => Rgb(0xb8, 0xa4, 0x0c),
        _ => Rgb(0xa9, 0xb0, 0xb8),
    };

    /// <summary>One shared brush per colour.</summary>
    public SolidColorBrush Brush(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            _brushes[color] = brush;
        }
        return brush;
    }

    public static Color Rgb(byte r, byte g, byte b) => ColorHelper.FromArgb(255, r, g, b);

    public static Color Rgba(byte r, byte g, byte b, double a) => ColorHelper.FromArgb((byte)Math.Round(a * 255), r, g, b);

    /// <summary>
    /// Takes the user's felt and note paper; raises <see cref="Changed"/> if
    /// that changes any colour, and says whether it did.
    /// </summary>
    public bool Follow(FeltSetting felt, PaperSetting paper)
    {
        if (felt == _felt && paper == _paper)
        {
            return false;
        }
        (_felt, _paper) = (felt, paper);
        return Refresh();
    }

    bool Refresh()
    {
        var before = Snapshot();
        Read();
        if (before == Snapshot())
        {
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    (bool, bool, Color, Color, Color, Color, Color, FeltTones, PaperTones, HintTones) Snapshot() =>
        (IsDark, IsHighContrast, Canvas, CanvasText, Highlight, LinkText, Accent, FeltTones, _note, _hint);

    void Read()
    {
        IsDark = _root.ActualTheme == ElementTheme.Dark;
        IsHighContrast = _accessibility.HighContrast;
        Canvas = _settings.UIElementColor(UIElementType.Window);
        CanvasText = _settings.UIElementColor(UIElementType.WindowText);
        Highlight = _settings.UIElementColor(UIElementType.Highlight);
        HighlightText = _settings.UIElementColor(UIElementType.HighlightText);
        ButtonFace = _settings.UIElementColor(UIElementType.ButtonFace);
        ButtonText = _settings.UIElementColor(UIElementType.ButtonText);
        LinkText = _settings.UIElementColor(UIElementType.Hotlight);
        Accent = _settings.GetColorValue(UIColorType.Accent);
        FeltTones = FeltTones.For(_felt, Accent.ToRgb(), IsDark);
        _note = PaperTones.For(_paper, IsDark);
        _hint = HintTones.For(_paper, IsDark);
    }
}
